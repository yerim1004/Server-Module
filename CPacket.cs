using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace serverModule
{

    public class CPacket
    {
        public CUserToken owner { get; private set; }
        public byte[] buffer { get; private set; }
        public int position { get; private set;}
        public int size { get; private set; }

        public Int16 protocol_id { get; private set; }
        public static CPacket create(Int16 protocol_id)
        {
            CPacket packet = new CPacket();

            packet.set_protocol(protocol_id);
            return packet;
        }
        public static void destroy(CPacket packet)
        {
            //CPacketBufferManager.push(packet);
        }
        public CPacket(ArraySegment<byte> buffer, CUserToken owner)
        {
            this.buffer = buffer.Array;

            this.position = Defines.HEADERSIZE;
            this.size = buffer.Count;

            this.protocol_id = pop_protocol_id();
            this.position = Defines.HEADERSIZE;

            this.owner = owner;
        }
        public CPacket (byte[] buffer, CUserToken owner)
        {
            this.buffer = buffer;

            this.position = Defines.HEADERSIZE;
            this.owner = owner;
        }
        public CPacket()
        {
            this.buffer = new byte[1024];
        }
        public Int16 pop_protocol_id()
        {
            return pop_int16();
        }
        public void copy_to(CPacket target)
        {
            target.set_protocol(this.protocol_id);
            target.overwrite(this.buffer, this.position);
        }
        public void overwrite(byte[] source, int position)
        {
            Array.Copy(source, this.buffer, source.Length);
            this.position = position;
        }
        public byte pop_byte()
        {
            byte data = this.buffer[this.position];
            this.position += sizeof(byte);
            return data;
        }
        public Int16 pop_int16()
        {
            Int16 data = BitConverter.ToInt16(this.buffer, this.position);
            this.position += sizeof(Int16);
            return data;
        }
        public Int32 pop_int32()
        {
            Int32 data = BitConverter.ToInt32(this.buffer, this.position);
            this.position += sizeof(Int32);
            return data;
        }
        public string pop_string()
        {
            Int16 len = BitConverter.ToInt16(this.buffer, this.position);
            this.position += sizeof(Int16);

            string data = System.Text.Encoding.UTF8.GetString(this.buffer, this.position, len);
            this.position += len;

            return data;
        }
        public float pop_float()
        {
            float data = BitConverter.ToSingle(this.buffer, this.position);
            this.position += sizeof(float);
            return data;
        }
        public double pop_double()
        {
            double data = BitConverter.ToDouble(buffer, position);
            position += sizeof(double);
            return data;
        }
        public void set_protocol(Int16 protocol_id)
        {
            this.protocol_id = protocol_id;

            this.position = Defines.HEADERSIZE;

            push_int16(protocol_id);
        }
        public void push_int16(Int16 data)
        {
            byte[] temp_buffer = BitConverter.GetBytes(data);
            temp_buffer.CopyTo(this.buffer, this.position);
            this.position += temp_buffer.Length;
        }
        public void record_size()
        {
            //헤더+바디 사이즈를 입력
            byte[] header = BitConverter.GetBytes(this.position);
            header.CopyTo(this.buffer, 0);
        }
        public void push(Int16 data)
        {
            byte[] temp_buffer = BitConverter.GetBytes(data);
            temp_buffer.CopyTo(this.buffer, this.position);
            this.position += temp_buffer.Length;
        }
        public void push(Int32 data)
        {
            byte[] temp_buffer = BitConverter.GetBytes(data);
            temp_buffer.CopyTo(this.buffer, this.position);
            this.position += temp_buffer.Length;
        }
        public void push(string data)
        {
            byte[] temp_buffer = Encoding.UTF8.GetBytes(data);

            Int16 len = (Int16)temp_buffer.Length;
            byte[] len_buffer = BitConverter.GetBytes(len);
            len_buffer.CopyTo(this.buffer, this.position);
            this.position += sizeof(Int16);

            temp_buffer.CopyTo(this.buffer, this.position);
            this.position += temp_buffer.Length;
        }
        public void push(float data)
        {
            byte[] temp_buffer = BitConverter.GetBytes(data);
            temp_buffer.CopyTo(this.buffer, this.position);
            this.position += temp_buffer.Length;
        }
        public void push(double data)
        {
            byte[] temp_buffer = BitConverter.GetBytes(data);
            temp_buffer.CopyTo(this.buffer, this.position);
            this.position += temp_buffer.Length;
        }
    }
    
    

}
