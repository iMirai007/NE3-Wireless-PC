using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NE3_Wireless_PC.Model
{
    public class Frame
    {
        private Header _header;
        private Dictionary<int, byte[]> _chunks;
        private bool _complete;
        public Frame(Header header)
        {
            _header = header;
            _chunks = new Dictionary<int, byte[]>();
            _complete = false;
        }

        public void Add(byte[] data)
        {
            Header header = new Header(data);
            byte[] chunkData = data.Skip(4).ToArray();  // Skip the first 4 bytes which are the header part

            // Check the flags to see if the last part of the chunk should be removed
            if ((header.Flags & 1) != 0)
            {
                // Print the last 5 bytes as hex if flag is set
                Console.WriteLine(BitConverter.ToString(chunkData.Skip(chunkData.Length - 5).ToArray()).Replace("-", ""));
                chunkData = chunkData.Take(chunkData.Length - 5).ToArray();  // Remove the last 5 bytes
            }

            _chunks[header.ChunkNumber - 1] = chunkData;

            // Check if the frame is complete
            if ((header.Flags & 1) != 0 && _chunks.Count == header.ChunkNumber)
            {
                _complete = true;
            }
        }

        public bool Complete()
        {
            return _complete;
        }
        public byte[] Data()
        {
            List<byte> data = new List<byte>();
            for (int i = 0; i < _chunks.Count; i++)
            {
                data.AddRange(_chunks[i]);
            }
            return data.ToArray();
        }

    }
}
