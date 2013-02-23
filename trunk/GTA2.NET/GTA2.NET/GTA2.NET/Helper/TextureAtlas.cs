﻿//Created: 28.01.2010

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Xml.Serialization;
using System.IO;
using Hiale.GTA2NET.Core.Helper;
using Hiale.GTA2NET.Core.Style;
using Rectangle=Microsoft.Xna.Framework.Rectangle;


namespace Hiale.GTA2NET.Helper
{
    /// <summary>
    /// Holds information where certail tiles or sprites are put on the image.
    /// </summary>
    [Serializable]
    public class TextureAtlas : IDisposable
    {
        public class ImageEntry
        {
            public int Index;
            public string FileName;
            public int X;
            public int Y;
            public int Width;
            public int Height;
            public int ZipEntryIndex;
            public int SameSpriteIndex;
        }

        public class ImageEntryComparer : IComparer<ImageEntry>
        {
            public bool CompareSize { get; set; }

            public int Compare(ImageEntry x, ImageEntry y)
            {
                if (CompareSize)
                {
                    int xSize = x.Height * 1024 + x.Width;
                    int ySize = y.Height * 1024 + y.Width;
                    return ySize.CompareTo(xSize);
                }
                return x.Index.CompareTo(y.Index);
            }
        }

        /// <summary>
        /// Image with all the tiles or sprites on it.
        /// </summary>
        [XmlIgnore]
        public Image Image { get; protected set; }

        /// <summary>
        /// Path to image file, used by serialization
        /// </summary>
        public string ImagePath { get; set; }

        protected string ImageDirName;

        protected ZipStorer ZipStore;

        protected List<ZipStorer.ZipFileEntry> ZipEntries;

        protected Dictionary<UInt32, int> CrcDictionary; //Helper list to find duplicate images.

        protected Graphics Graphics;

        protected TextureAtlas()
        {
            //needed by xml serializer
        }

        protected TextureAtlas(string imagePath, ZipStorer zipStore)
        {
            CrcDictionary = new Dictionary<UInt32, int>();
            ImagePath = imagePath;
            ZipStore = zipStore;
        }

        protected List<ImageEntry> CreateImageEntries()
        {
            List<ImageEntry> entries = new List<ImageEntry>();
            CrcDictionary.Clear();
            ZipEntries = ZipStore.ReadCentralDir();
            for (int i = 0; i < ZipEntries.Count; i++)
            {
                if (!ZipEntries[i].FilenameInZip.StartsWith(ImageDirName))
                    continue;
                Bitmap source = GetBitmapFromZip(ZipStore, ZipEntries[i]);
                ImageEntry entry = new ImageEntry();
                if (!CrcDictionary.ContainsKey(ZipEntries[i].Crc32))
                    CrcDictionary.Add(ZipEntries[i].Crc32, i);
                else
                    entry.SameSpriteIndex = CrcDictionary[ZipEntries[i].Crc32];
                entry.Index = i;
                entry.FileName = ParsePath(ZipEntries[i].FilenameInZip);
                entry.Width = source.Width + 2;  // Include a single pixel padding around each sprite, to avoid filtering problems if the sprite is scaled or rotated.
                entry.Height = source.Height + 2;
                entry.ZipEntryIndex = i;
                entries.Add(entry);
                source.Dispose();
            }
            return entries;
        }

        protected static Bitmap GetBitmapFromZip(ZipStorer zipStore, ZipStorer.ZipFileEntry zipFileEntry)
        {
            MemoryStream memoryStream = new MemoryStream((int)zipFileEntry.FileSize);
            zipStore.ExtractFile(zipFileEntry, memoryStream);
            memoryStream.Position = 0;
            Bitmap bmp = (Bitmap)Image.FromStream(memoryStream);
            memoryStream.Close();
            return bmp;
        }

        protected static void FindFreeSpace(List<ImageEntry> entries, ref int outputWidth, ref int outputHeight)
        {
            outputWidth = GuessOutputWidth(entries);
            outputHeight = 0;

            // Choose positions for each sprite, one at a time.
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].SameSpriteIndex > 0)
                    continue;
                PositionSprite(entries, i, outputWidth);
                outputHeight = Math.Max(outputHeight, entries[i].Y + entries[i].Height);
            }
        }

        protected void CreateOutputBitmap(int width, int height)
        {
            Image = new Bitmap(width, height);
            Graphics = Graphics.FromImage(Image);
        }

        protected Rectangle PaintAndGetRectangle(ImageEntry entry)
        {
            Bitmap source = GetBitmapFromZip(ZipStore, ZipEntries[entry.ZipEntryIndex]);
            Graphics.DrawImageUnscaled(source, entry.X + 1, entry.Y + 1);
            source.Dispose();
            return new Rectangle(entry.X + 1, entry.Y + 1, entry.Width - 2, entry.Height - 2);
        }

        public virtual void BuildTextureAtlas()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Heuristic guesses what might be a good output width for a list of sprites.
        /// </summary>
        protected static int GuessOutputWidth(ICollection<ImageEntry> entries)
        {
            // Gather the widths of all our sprites into a temporary list.
            List<int> widths = new List<int>();

            foreach (ImageEntry entry in entries)
            {
                widths.Add(entry.Width);
            }

            // Sort the widths into ascending order.
            widths.Sort();

            // Extract the maximum and median widths.
            int maxWidth = widths[widths.Count - 1];
            int medianWidth = widths[widths.Count / 2];

            // Heuristic assumes an NxN grid of median sized sprites.
            int width = medianWidth * (int)Math.Round(Math.Sqrt(entries.Count));

            // Make sure we never choose anything smaller than our largest sprite.
            return Math.Max(width, maxWidth);
        }

        /// <summary>
        /// Works out where to position a single sprite.
        /// </summary>
       protected static void PositionSprite(List<ImageEntry> entries, int index, int outputWidth)
        {
            int x = 0;
            int y = 0;

            while (true)
            {
                // Is this position free for us to use?
                int intersects = FindIntersectingSprite(entries, index, x, y);

                if (intersects < 0)
                {
                    entries[index].X = x;
                    entries[index].Y = y;

                    return;
                }

                // Skip past the existing sprite that we collided with.
                x = entries[intersects].X + entries[intersects].Width;

                // If we ran out of room to move to the right,
                // try the next line down instead.
                if (x + entries[index].Width > outputWidth)
                {
                    x = 0;
                    y++;
                }
            }
        }

        /// <summary>
        /// Checks if a proposed sprite position collides with anything
        /// that we already arranged.
        /// </summary>
        protected static int FindIntersectingSprite(List<ImageEntry> entries, int index, int x, int y)
        {
            int w = entries[index].Width;
            int h = entries[index].Height;

            for (int i = 0; i < index; i++)
            {
                if (entries[i].X >= x + w)
                    continue;

                if (entries[i].X + entries[i].Width <= x)
                    continue;

                if (entries[i].Y >= y + h)
                    continue;

                if (entries[i].Y + entries[i].Height <= y)
                    continue;

                return i;
            }

            return -1;
        }

        private static string ParsePath(string path)
        {
            int pos = path.LastIndexOf('/');
            return path.Substring(pos + 1, path.Length - pos - Style.Png.Length - 1);
        }

        public void Serialize(string path)
        {
            TextWriter textWriter = new StreamWriter(path);
            XmlSerializer serializer = new XmlSerializer(GetType());
            serializer.Serialize(textWriter, this);
            textWriter.Close();
        }

        public static TextureAtlas Deserialize(string path, Type type)
        {
            TextReader textReader = new StreamReader(path);
            XmlSerializer deserializer = new XmlSerializer(type);
            TextureAtlas atlas = (TextureAtlas)deserializer.Deserialize(textReader);
            textReader.Close();
            return atlas;
        }

        /// <summary>
        /// Disposes the image when not needed anymore.
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (Image != null)
                    Image.Dispose();
                if (Graphics != null)
                    Graphics.Dispose();
            }
            catch (Exception) { }
        }

    }

    public class TextureAtlasTiles : TextureAtlas
    {
        public SerializableDictionary<int, Rectangle> TileDictionary { get; set; }

        private TextureAtlasTiles()
        {
            //this constructor is needed by xml serializer
        }

        public TextureAtlasTiles(string imagePath, ZipStorer zipStore) : base(imagePath, zipStore)
        {
            ImageDirName = Style.TilesZipDir;
        }

        public override void BuildTextureAtlas()
        {
            List<ImageEntry> entries = CreateImageEntries();
            int outputWidth = GuessOutputWidth(entries);
            int outputHeight = 0;
            FindFreeSpace(entries, ref outputWidth, ref outputHeight);
            CreateOutputBitmap(outputWidth, outputHeight);
            TileDictionary = new SerializableDictionary<int, Rectangle>();
            for (int i = 0; i < entries.Count; i++)
            {
                Rectangle rect = entries[i].SameSpriteIndex == 0 ? PaintAndGetRectangle(entries[i]) : TileDictionary[entries[i].SameSpriteIndex];
                try
                {
                    int index = int.Parse(entries[i].FileName);
                    TileDictionary.Add(index, rect);
                }
                catch (Exception)
                {
                    continue;
                }
            }
            Image.Save(ImagePath, ImageFormat.Png);
        }
    }

    public class TextureAtlasSprites : TextureAtlas
    {
        public SerializableDictionary<SpriteItem, Rectangle> SpriteDictionary { get; set; }

        private readonly Dictionary<int, SpriteItem> _duplicateDictionary; //Helper list to find duplicate images.

        private TextureAtlasSprites()
        {
            //this constructor is needed by xml serializer
        }

        public TextureAtlasSprites(string imagePath, ZipStorer zipStore) : base(imagePath, zipStore)
        {
            ImageDirName = Style.SpritesZipDir;
            _duplicateDictionary = new Dictionary<int, SpriteItem>();
        }

        public override void BuildTextureAtlas()
        {
            List<ImageEntry> entries = CreateImageEntries();

            // Sort so the largest sprites get arranged first.
            ImageEntryComparer comparer = new ImageEntryComparer {CompareSize = true};
            entries.Sort(comparer);

            int outputWidth = 0;
            int outputHeight = 0;
            FindFreeSpace(entries, ref outputWidth, ref outputHeight);

            // Sort the sprites back into index order.
            comparer.CompareSize = false;
            entries.Sort(comparer);

            CreateOutputBitmap(outputWidth, outputHeight);
            SpriteDictionary = new SerializableDictionary<SpriteItem, Rectangle>();
            for (int i = 0; i < entries.Count; i++)
            {
                Rectangle rect = entries[i].SameSpriteIndex == 0 ? PaintAndGetRectangle(entries[i]) : SpriteDictionary[_duplicateDictionary[entries[i].SameSpriteIndex]];
                string fileName = entries[i].FileName;
                SpriteItem item;
                try
                {
                    item = ParseFileName(fileName);
                }
                catch (Exception)
                {
                    continue;
                }
                _duplicateDictionary.Add(entries[i].Index, item);
                SpriteDictionary.Add(item, rect);
            }
            Image.Save(ImagePath, ImageFormat.Png);
        }

        private static SpriteItem ParseFileName(string fileName)
        {
            SpriteItem item = new SpriteItem();
            string[] parts = fileName.Split('_');
            item.Sprite = int.Parse(parts[0]);
            item.Remap = -1;
            if (parts.Length == 3)
            {
                item.Model = int.Parse(parts[1]);
                item.Remap = int.Parse(parts[2]);
            }
            return item;
        }
    }
}
