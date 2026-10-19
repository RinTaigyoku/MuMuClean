using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class BuildIcon
{
    static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: BuildIcon <input.png> <output.ico>");
            return 2;
        }

        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        using (var source = new Bitmap(args[0]))
        {
            foreach (int size in sizes)
            {
                using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var stream = new MemoryStream())
                {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source, 0, 0, size, size);
                    bitmap.Save(stream, ImageFormat.Png);
                    images.Add(stream.ToArray());
                }
            }
        }

        using (var output = new BinaryWriter(File.Create(args[1])))
        {
            output.Write((ushort)0);
            output.Write((ushort)1);
            output.Write((ushort)images.Count);
            int offset = 6 + images.Count * 16;
            for (int i = 0; i < images.Count; i++)
            {
                int size = sizes[i];
                output.Write((byte)(size == 256 ? 0 : size));
                output.Write((byte)(size == 256 ? 0 : size));
                output.Write((byte)0);
                output.Write((byte)0);
                output.Write((ushort)1);
                output.Write((ushort)32);
                output.Write(images[i].Length);
                output.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] image in images) output.Write(image);
        }
        return 0;
    }
}
