using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

namespace StreamDeckFree
{
    public static class ImageEncoder
    {
        private const int ButtonWidth = 101;
        private const int ButtonHeight = 114;

        public static byte[] GetJpegBytes(Image sourceImage)
        {
            using (Image cloned = sourceImage.Clone(ctx => ctx.Resize(ButtonWidth, ButtonHeight)))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    var encoder = new JpegEncoder { Quality = 80 };
                    cloned.Save(ms, encoder);
                    return ms.ToArray();
                }
            }
        }
    }
}