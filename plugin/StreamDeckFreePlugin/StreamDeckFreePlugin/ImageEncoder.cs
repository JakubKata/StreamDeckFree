using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.CottleIntegration;
using MacroButton = SuchByte.MacroDeck.ActionButton.ActionButton;
using DrawingColor = System.Drawing.Color;
using DrawingImage = System.Drawing.Image;
using DrawingEncoder = System.Drawing.Imaging.Encoder;

namespace StreamDeckFree
{
    public sealed class Rgb565Image
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Bytes { get; }

        public Rgb565Image(int width, int height, byte[] bytes)
        {
            Width = width;
            Height = height;
            Bytes = bytes ?? Array.Empty<byte>();
        }
    }

    public static class ImageEncoder
    {
        private const int MaxPayloadBytes = CydDevice.MaxFirmwarePayload - 1;

        public static Rgb565Image RenderButtonRgb565(MacroButton button, int width, int height)
        {
            using Bitmap canvas = RenderButtonBitmap(button, width, height);
            return new Rgb565Image(canvas.Width, canvas.Height, EncodeRgb565BigEndian(canvas));
        }

        public static Rgb565Image RenderEmptyRgb565(int width, int height)
        {
            using Bitmap canvas = RenderEmptyBitmap(width, height);
            return new Rgb565Image(canvas.Width, canvas.Height, EncodeRgb565BigEndian(canvas));
        }

        public static Rgb565Image RenderErrorRgb565(int width, int height, string text)
        {
            using Bitmap canvas = RenderErrorBitmap(width, height, text);
            return new Rgb565Image(canvas.Width, canvas.Height, EncodeRgb565BigEndian(canvas));
        }

        // Kept only for compatibility/diagnostics. Plugin v5 sends RGB565 chunks, not JPEG.
        public static byte[] RenderButton(MacroButton button, int width, int height)
        {
            using Bitmap canvas = RenderButtonBitmap(button, width, height);
            return EncodeJpeg(canvas);
        }

        public static byte[] RenderEmpty(int width, int height)
        {
            using Bitmap canvas = RenderEmptyBitmap(width, height);
            return EncodeJpeg(canvas);
        }

        public static byte[] RenderError(int width, int height, string text)
        {
            using Bitmap canvas = RenderErrorBitmap(width, height, text);
            return EncodeJpeg(canvas);
        }

        private static Bitmap RenderButtonBitmap(MacroButton button, int width, int height)
        {
            if (button == null)
            {
                return RenderEmptyBitmap(width, height);
            }

            bool state = button.State;
            DrawingColor backColor = state ? button.BackColorOn : button.BackColorOff;
            string iconBase64 = state ? button.IconOn : button.IconOff;
            ButtonLabel label = state ? button.LabelOn : button.LabelOff;
            bool hasLabel = label != null && (!string.IsNullOrWhiteSpace(label.LabelText) || !string.IsNullOrWhiteSpace(label.LabelBase64));

            Bitmap canvas = CreateCanvas(width, height, backColor);
            using Graphics g = Graphics.FromImage(canvas);
            ConfigureGraphics(g);

            DrawIcon(g, iconBase64, width, height, hasLabel);
            DrawLabel(g, label, width, height);
            DrawBorder(g, width, height, DrawingColor.FromArgb(70, 70, 70));

            return canvas;
        }

        private static Bitmap RenderEmptyBitmap(int width, int height)
        {
            Bitmap canvas = CreateCanvas(width, height, DrawingColor.FromArgb(35, 35, 35));
            using Graphics g = Graphics.FromImage(canvas);
            ConfigureGraphics(g);
            DrawBorder(g, width, height, DrawingColor.FromArgb(55, 55, 55));
            return canvas;
        }

        private static Bitmap RenderErrorBitmap(int width, int height, string text)
        {
            Bitmap canvas = CreateCanvas(width, height, DrawingColor.FromArgb(80, 0, 0));
            using Graphics g = Graphics.FromImage(canvas);
            ConfigureGraphics(g);

            using Font font = new Font(FontFamily.GenericSansSerif, Math.Max(7, height / 9f), FontStyle.Bold);
            using Brush brush = new SolidBrush(DrawingColor.White);
            using StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text ?? "ERR", font, brush, new RectangleF(2, 2, width - 4, height - 4), sf);
            DrawBorder(g, width, height, DrawingColor.FromArgb(120, 0, 0));

            return canvas;
        }

        private static Bitmap CreateCanvas(int width, int height, DrawingColor color)
        {
            width = Math.Max(8, width);
            height = Math.Max(8, height);

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(bitmap);
            g.Clear(color);
            return bitmap;
        }

        private static void ConfigureGraphics(Graphics g)
        {
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }

        private static void DrawIcon(Graphics g, string base64, int width, int height, bool reserveLabelSpace)
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                return;
            }

            using Bitmap icon = TryLoadBitmap(base64);
            if (icon == null)
            {
                return;
            }

            int padding = Math.Max(3, Math.Min(width, height) / 12);
            int labelReserve = reserveLabelSpace ? Math.Max(18, height / 4) : 0;
            int maxW = Math.Max(1, width - padding * 2);
            int maxH = Math.Max(1, height - padding * 2 - labelReserve);

            Rectangle dest = FitInside(icon.Width, icon.Height, new Rectangle(padding, padding, maxW, maxH));
            g.DrawImage(icon, dest);
        }

        private static void DrawLabel(Graphics g, ButtonLabel label, int width, int height)
        {
            if (label == null)
            {
                return;
            }

            string text = label.LabelText ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    text = TemplateManager.RenderTemplate(text);
                }
                catch
                {
                    // Use raw text if a template fails.
                }
            }

            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(label.LabelBase64))
            {
                DrawLabelBitmap(g, label.LabelBase64, width, height);
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            float fontSize = Math.Max(7.0f, label.Size * Math.Min(width, height) / 40.0f);
            FontFamily family = GetFontFamily(label.FontFamily);

            using Font font = new Font(family, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using StringFormat sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = label.LabelPosition switch
                {
                    ButtonLabelPosition.TOP => StringAlignment.Near,
                    ButtonLabelPosition.CENTER => StringAlignment.Center,
                    _ => StringAlignment.Far
                },
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.LineLimit
            };

            RectangleF rect = new RectangleF(3, 3, width - 6, height - 6);

            using Brush shadow = new SolidBrush(DrawingColor.FromArgb(210, 0, 0, 0));
            using Brush brush = new SolidBrush(label.LabelColor);

            RectangleF shadowRect = new RectangleF(rect.X + 1, rect.Y + 1, rect.Width, rect.Height);
            g.DrawString(text, font, shadow, shadowRect, sf);
            g.DrawString(text, font, brush, rect, sf);
        }

        private static void DrawLabelBitmap(Graphics g, string base64, int width, int height)
        {
            using Bitmap label = TryLoadBitmap(base64);
            if (label == null)
            {
                return;
            }

            g.DrawImage(label, new Rectangle(0, 0, width, height));
        }

        private static void DrawBorder(Graphics g, int width, int height, DrawingColor color)
        {
            using Pen pen = new Pen(color, 1);
            g.DrawRectangle(pen, 0, 0, width - 1, height - 1);
        }

        private static Bitmap TryLoadBitmap(string base64)
        {
            try
            {
                string cleaned = NormalizeBase64(base64);
                byte[] bytes = Convert.FromBase64String(cleaned);
                using MemoryStream ms = new MemoryStream(bytes);
                using DrawingImage loaded = DrawingImage.FromStream(ms, true, true);
                return new Bitmap(loaded);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeBase64(string base64)
        {
            string cleaned = new string(base64.Where(c => !char.IsWhiteSpace(c)).ToArray());

            // Some Macro Deck/image sources expose data URLs instead of plain base64.
            int comma = cleaned.IndexOf(',');
            if (comma >= 0 && cleaned.Substring(0, comma).IndexOf("base64", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                cleaned = cleaned.Substring(comma + 1);
            }

            int remainder = cleaned.Length % 4;
            if (remainder != 0)
            {
                cleaned = cleaned.PadRight(cleaned.Length + (4 - remainder), '=');
            }
            return cleaned;
        }

        private static Rectangle FitInside(int sourceWidth, int sourceHeight, Rectangle bounds)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return bounds;
            }

            double scale = Math.Min(bounds.Width / (double)sourceWidth, bounds.Height / (double)sourceHeight);
            int w = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int h = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            int x = bounds.X + (bounds.Width - w) / 2;
            int y = bounds.Y + (bounds.Height - h) / 2;
            return new Rectangle(x, y, w, h);
        }

        private static FontFamily GetFontFamily(string familyName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(familyName))
                {
                    return new FontFamily(familyName);
                }
            }
            catch
            {
                // ignored
            }

            return FontFamily.GenericSansSerif;
        }

        private static byte[] EncodeRgb565BigEndian(Bitmap bitmap)
        {
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                int width = bitmap.Width;
                int height = bitmap.Height;
                int stride = Math.Abs(data.Stride);
                byte[] source = new byte[stride * height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, source, 0, source.Length);

                byte[] output = new byte[width * height * 2];
                int outIndex = 0;

                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int src = rowOffset + x * 3;
                        byte b = source[src];
                        byte g = source[src + 1];
                        byte r = source[src + 2];

                        ushort rgb565 = (ushort)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
                        output[outIndex++] = (byte)(rgb565 >> 8);
                        output[outIndex++] = (byte)(rgb565 & 0xFF);
                    }
                }

                return output;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static byte[] EncodeJpeg(Bitmap bitmap)
        {
            long[] qualities = { 75, 65, 55, 45, 35, 25 };
            byte[] result = Array.Empty<byte>();

            foreach (long quality in qualities)
            {
                result = EncodeJpeg(bitmap, quality);
                if (result.Length <= MaxPayloadBytes)
                {
                    return result;
                }
            }

            return result;
        }

        private static byte[] EncodeJpeg(Bitmap bitmap, long quality)
        {
            using MemoryStream ms = new MemoryStream();
            ImageCodecInfo codec = ImageCodecInfo.GetImageDecoders()
                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

            using EncoderParameters encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(DrawingEncoder.Quality, quality);
            bitmap.Save(ms, codec, encoderParameters);
            return ms.ToArray();
        }
    }
}
