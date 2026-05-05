namespace CloudScope
{
    public interface IDepthPicker
    {
        float ReadDepth(int x, int y);
        int ReadDepthWindow(int x, int y, int width, int height, float[] destination);
    }
}
