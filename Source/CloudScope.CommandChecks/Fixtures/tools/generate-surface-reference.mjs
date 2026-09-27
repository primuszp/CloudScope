import { pathToFileURL } from 'node:url';
// Run with Node 24+: node generate-surface-reference.mjs /path/to/reference/SurfaceReconstruction.ts
const { reconstructSurface } = await import(pathToFileURL(process.argv[2]).href);
import fs from 'node:fs';
const fixtures=[];
for (const kind of ['curved','sparse']) {
 const positions=[];
 for(let y=0;y<6;y++)for(let x=0;x<6;x++)positions.push(x+0.13*Math.sin(y*3+x),y+0.09*Math.cos(x*2+y),0.04*x*x+0.07*y*y+0.03*Math.sin(x+y));
 if(kind==='sparse')positions.push(50,50,50);
 const input=Array.from(new Float32Array(positions));
 for(const maximumEdge of [0,1.8]){
 const result=await reconstructSurface(new Float32Array(input),{kNeighbors:8,...(maximumEdge===0?{}:{maxEdgeLength:maximumEdge})});
 fixtures.push({name:kind+'-'+maximumEdge,positions:input,neighbors:8,maximumEdge,indices:Array.from(result.indices),normals:Array.from(result.normals)});
 }
}
fs.writeFileSync(new URL('../surface-reference.json', import.meta.url),JSON.stringify(fixtures));
console.log(fixtures.map(f=>({name:f.name,triangles:f.indices.length/3})));
