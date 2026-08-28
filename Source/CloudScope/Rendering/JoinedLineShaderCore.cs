namespace CloudScope.Rendering;

/// <summary>
/// The screen-space expansion shared by the OpenGL and Metal joined-line shaders.
/// </summary>
/// <remarks>
/// Neither core-profile OpenGL nor Metal can widen a native line (see <see cref="LineWidth"/>),
/// so every line in the viewer is a screen-space quad built in the vertex shader. Keeping the
/// expansion in one place - GLSL and MSL only supply a header that maps the vector type names -
/// guarantees a polyline, a gizmo circle and the pivot rings are pixel-identical on both
/// backends. Geometry is emitted overlap-free: a segment stops at the miter intersection on the
/// inside of a bend, at the plain normal offset on the outside, and a round-join fan fills the
/// remaining wedge. Overlapping quads would blend twice and knot every joint of a translucent
/// polyline, which is exactly what a plain per-segment capsule list does.
/// </remarks>
internal static class JoinedLineShaderCore
{
    public const string GlslHeader = @"
#define ATAN2(y, x) atan(y, x)
";

    public const string MetalHeader = @"
#define vec2 float2
#define vec3 float3
#define vec4 float4
#define mat4 float4x4
#define ATAN2(y, x) atan2(y, x)
#define inversesqrt rsqrt
";

    /// <summary>JOINED_LINE_ARC_SEGMENTS must match <see cref="PolylineRenderGeometry.JoinArcSegments"/>.</summary>
    public const string Source = @"
#define JOINED_LINE_ARC_SEGMENTS 8.0
#define JOINED_LINE_UNBOUNDED 1e30

struct JoinedLineVertex
{
    vec4 position;   // clip space
    vec2 coord;      // pixels: along/across the segment, or radial offset inside a join
    vec2 limits;     // pixels: the along range that is body rather than round cap
    float depth;     // pixels: how deep inside the stroke this corner is known to be
};

vec2 joinedLinePerpendicular(vec2 v) { return vec2(-v.y, v.x); }

bool joinedLineSamePoint(vec4 a, vec4 b)
{
    vec3 delta = a.xyz / a.w - b.xyz / b.w;
    return dot(delta, delta) < 1e-12;
}

// Offset of one quad corner at a joint (xy), plus a flag (z) telling whether this is the
// inner corner. The outside of the bend keeps the plain normal offset so the round-join fan
// can fill the wedge; the inside moves to the intersection of the two offset edges, which is
// what makes neighbouring quads tile instead of overlap. The inner corner is a miter tip
// deep inside the stroke, so it is marked and never faded by the edge coverage.
vec3 joinedLineJointOffset(
    vec2 incoming, vec2 outgoing, vec2 normal,
    float side, float halfWidth, float incomingLength, float outgoingLength)
{
    float turn = incoming.x * outgoing.y - incoming.y * outgoing.x;
    float innerSide = turn >= 0.0 ? 1.0 : -1.0;
    vec2 plain = normal * side * halfWidth;
    if (abs(turn) < 1e-6 && dot(incoming, outgoing) > 0.0)
        return vec3(plain, 0.0);
    if (side != innerSide)
        return vec3(plain, 0.0);

    vec2 incomingNormal = joinedLinePerpendicular(incoming);
    vec2 miterSum = incomingNormal + joinedLinePerpendicular(outgoing);
    if (dot(miterSum, miterSum) < 1e-12)
        return vec3(plain, 0.0);   // a 180 degree reversal has no intersection

    vec2 miter = normalize(miterSum);
    float miterLength = halfWidth / max(abs(dot(miter, incomingNormal)), 0.1);
    // Both segments clamp against the same shorter neighbour, so their inner corners stay
    // on one point; without the clamp a sharp bend folds the quad past its far end.
    miterLength = min(miterLength, min(incomingLength, outgoingLength));
    return vec3(miter * innerSide * miterLength, 1.0);
}

// vertexIndex 0..3 of a triangle strip: 0 and 1 at the start point, 2 and 3 at the end.
JoinedLineVertex joinedLineSegment(
    vec4 clipPrevious, vec4 clipStart, vec4 clipEnd, vec4 clipNext,
    vec2 viewport, float width, int vertexIndex)
{
    JoinedLineVertex result;
    bool atStart = vertexIndex < 2;
    float side = (vertexIndex - 2 * (vertexIndex / 2)) == 0 ? -1.0 : 1.0;
    vec4 clipHere = atStart ? clipStart : clipEnd;
    result.position = clipHere;
    result.coord = vec2(0.0, 0.0);
    result.limits = vec2(0.0, 0.0);
    result.depth = 0.0;

    // Behind the eye the perspective divide is meaningless; collapsing the quad keeps the
    // segment from smearing across the whole viewport.
    if (clipStart.w <= 0.0 || clipEnd.w <= 0.0)
        return result;

    vec2 halfViewport = max(viewport, vec2(1.0, 1.0)) * 0.5;
    vec2 pStart = clipStart.xy / clipStart.w * halfViewport;
    vec2 pEnd = clipEnd.xy / clipEnd.w * halfViewport;
    vec2 delta = pEnd - pStart;
    float length2 = dot(delta, delta);
    vec2 direction = length2 > 1e-12 ? delta * inversesqrt(length2) : vec2(1.0, 0.0);
    vec2 normal = joinedLinePerpendicular(direction);
    float segmentLength = sqrt(length2);
    // The half pixel carries the analytic edge fringe; the fragment measures coverage
    // against the requested width, so geometry and fringe stay consistent everywhere.
    float halfWidth = width * 0.5 + 0.5;

    bool startJoins = clipPrevious.w > 0.0 && !joinedLineSamePoint(clipPrevious, clipStart);
    bool endJoins = clipNext.w > 0.0 && !joinedLineSamePoint(clipNext, clipEnd);

    vec2 here = atStart ? pStart : pEnd;
    vec3 offset;
    if (atStart ? startJoins : endJoins)
    {
        vec4 clipOther = atStart ? clipPrevious : clipNext;
        vec2 other = clipOther.xy / clipOther.w * halfViewport;
        float otherLength = length(other - here);
        vec2 incoming = atStart ? normalize(here - other) : direction;
        vec2 outgoing = atStart ? direction : normalize(other - here);
        offset = joinedLineJointOffset(
            incoming, outgoing, normal, side, halfWidth,
            atStart ? otherLength : segmentLength,
            atStart ? segmentLength : otherLength);
    }
    else
    {
        // A free end is extended by the half width and rounded by the fragment shader.
        offset = vec3(normal * side * halfWidth + direction * (atStart ? -halfWidth : halfWidth), 0.0);
    }

    vec2 point = here + offset.xy;
    // A miter tip sits on both offset lines at once, so its own segment measures it as an
    // edge fragment. Marking it as fully interior keeps the corner filled instead of
    // tapering into a transparent notch that widens as the bend gets sharper.
    result.depth = offset.z > 0.5 ? 0.0 : halfWidth;
    result.position = vec4(
        clipHere.xy + (point - here) / halfViewport * clipHere.w, clipHere.z, clipHere.w);
    vec2 fromStart = point - pStart;
    result.coord = vec2(dot(fromStart, direction), dot(fromStart, normal));
    result.limits = vec2(
        startJoins ? -JOINED_LINE_UNBOUNDED : 0.0,
        endJoins ? JOINED_LINE_UNBOUNDED : segmentLength);
    return result;
}

// A round join drawn as a triangle list fanning from the inner intersection point over the
// outer arc, so it exactly covers the wedge the two segment quads leave open.
JoinedLineVertex joinedLineJoin(
    vec4 clipPrevious, vec4 clipJoint, vec4 clipNext,
    vec2 viewport, float width, int vertexIndex)
{
    JoinedLineVertex result;
    result.position = clipJoint;
    result.coord = vec2(0.0, 0.0);
    result.limits = vec2(0.0, 0.0);
    result.depth = 0.0;
    if (clipPrevious.w <= 0.0 || clipJoint.w <= 0.0 || clipNext.w <= 0.0)
        return result;

    vec2 halfViewport = max(viewport, vec2(1.0, 1.0)) * 0.5;
    vec2 pPrevious = clipPrevious.xy / clipPrevious.w * halfViewport;
    vec2 pJoint = clipJoint.xy / clipJoint.w * halfViewport;
    vec2 pNext = clipNext.xy / clipNext.w * halfViewport;
    vec2 incomingDelta = pJoint - pPrevious;
    vec2 outgoingDelta = pNext - pJoint;
    float incomingLength = length(incomingDelta);
    float outgoingLength = length(outgoingDelta);
    if (incomingLength < 1e-6 || outgoingLength < 1e-6)
        return result;

    vec2 incoming = incomingDelta / incomingLength;
    vec2 outgoing = outgoingDelta / outgoingLength;
    float turn = incoming.x * outgoing.y - incoming.y * outgoing.x;
    float angle = ATAN2(turn, dot(incoming, outgoing));
    if (abs(angle) < 1e-4)
        return result;   // collinear: the segment quads already meet without a gap

    float innerSide = turn >= 0.0 ? 1.0 : -1.0;
    float halfWidth = width * 0.5 + 0.5;
    vec2 incomingNormal = joinedLinePerpendicular(incoming);

    int triangle = vertexIndex / 3;
    int corner = vertexIndex - triangle * 3;
    vec2 point;
    if (corner == 0)
    {
        // The fan starts at the miter tip the two segment quads also stop at, so the three
        // pieces tile exactly. Like there, the tip is interior and must not fade.
        point = pJoint + joinedLineJointOffset(
            incoming, outgoing, incomingNormal, innerSide, halfWidth,
            incomingLength, outgoingLength).xy;
        result.depth = 0.0;
    }
    else
    {
        result.depth = halfWidth;
        float step = float(triangle + corner - 1) / JOINED_LINE_ARC_SEGMENTS;
        vec2 spoke = incomingNormal * (-innerSide) * halfWidth;
        float rotation = angle * step;
        float cosine = cos(rotation);
        float sine = sin(rotation);
        point = pJoint + vec2(
            spoke.x * cosine - spoke.y * sine,
            spoke.x * sine + spoke.y * cosine);
    }

    result.position = vec4(
        clipJoint.xy + (point - pJoint) / halfViewport * clipJoint.w, clipJoint.z, clipJoint.w);
    result.coord = point - pJoint;
    return result;
}

// Distance to the ideal capsule, in pixels, turned into an antialiased edge. A join passes
// limits (0, 0), which reduces the measurement to the radius around the joint.
float joinedLineCoverage(vec2 coord, vec2 limits, float depth, float width)
{
    float outside = max(max(limits.x - coord.x, coord.x - limits.y), 0.0);
    float distanceToLine = min(length(vec2(outside, coord.y)), depth);
    float halfWidth = width * 0.5;
    return 1.0 - smoothstep(halfWidth - 0.5, halfWidth + 0.5, distanceToLine);
}
";
}
