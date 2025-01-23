using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NE3_Wireless_PC.Model
{
    public class Header
    {
        public byte FrameNumber { get; private set; }
        public byte Flags { get; private set; }
        public byte ChunkNumber { get; private set; }
        public byte Flags2 { get; private set; }

        public Header(byte[] packet)
        {
            // Unpack the first 4 bytes to initialize the fields
            if (packet.Length >= 4)
            {
                FrameNumber = packet[0];
                Flags = packet[1];
                ChunkNumber = packet[2];
                Flags2 = packet[3];
            }
        }
    }
}
