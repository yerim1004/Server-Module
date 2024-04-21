using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace serverModule
{
    class Defines
    {
        public static readonly short HEADERSIZE = 4;
    }

    public delegate void CompletedMessageCallback(ArraySegment<byte> buffer);

    //[헤더][바디] 구조의 데이터를 파싱?하는 클래스
    //헤더 : Defines.HEADERSIZE에 정의된 타입만큼의 크기를 갖는다.
    //       2바이트의 경우 Int16, 4바이트는 Int32로 처리한다.
    class CMessageResolver
    {
        int message_size;

        byte[] message_buffer = new byte[1024];

        int current_position;

        int position_to_read;

        int remain_bytes;

        public CMessageResolver()
        {
            this.message_size = 0;
            this.current_position = 0;
            this.position_to_read = 0;
            this.remain_bytes = 0;
        }

        //다 읽었으면 true, 데이터가 모자라서 못 읽었으면 false를 리턴한다.
        bool read_until(byte[] buffer, ref int src_position)
        {
            int copy_size = this.position_to_read - this.current_position;

            if(this.remain_bytes < copy_size)
            {
                copy_size = this.remain_bytes;
            }

            Array.Copy(buffer, src_position, this.message_buffer, this.current_position, copy_size);

            //원본 버퍼 포지션 이동
            src_position += copy_size;

            //타겟 버퍼 포지션 이동
            this.current_position += copy_size;

            //남은 바이트 수
            this.remain_bytes -= copy_size;

            if (this.current_position < this.position_to_read)
            {
                return false;
            }

            return true;
        }
        public void on_receive(byte[] buffer, int offset, int transffered, CompletedMessageCallback callback)
        {
            this.remain_bytes = transffered;

            //원본 버퍼의 포지션
            int src_position = offset;

            while (this.remain_bytes > 0)
            {
                bool completed = false;

                if (this.current_position < Defines.HEADERSIZE)
                {
                    this.position_to_read = Defines.HEADERSIZE;

                    completed = read_until(buffer, ref src_position);
                    if (!completed)
                    {
                        return;
                    }
                    this.message_size = get_total_message_size();

                    if(this.message_size <-0)
                    {
                        clear_buffer();
                        return;
                    }

                    this.position_to_read = this.message_size;

                    if(this.remain_bytes <= 0)
                    {
                        return;
                    }
                }

                completed = read_until(buffer, ref src_position);

                if (completed)
                {
                    byte[] clone = new byte[this.position_to_read];
                    Array.Copy(this.message_buffer, clone, this.position_to_read);
                    clear_buffer();
                    callback(new ArraySegment<byte>(clone, 0, this.position_to_read));
                }
            }
        }
        //헤더+바디 사이즈를 구한다.
        //패킷 헤더에 이미 전체 메시지 사이즈가 계산되어 있으므로 헤더 크기에 맞게 변환을 시켜준ek.
        int get_total_message_size()
        {
            if(Defines.HEADERSIZE == 2)
            {
                return BitConverter.ToInt16(this.message_buffer, 0);
            }
            else if(Defines.HEADERSIZE == 4)
            {
                return BitConverter.ToInt32(this.message_buffer, 0);
            }

            return 0;
        }
        
        public void clear_buffer()
        {
            Array.Clear(this.message_buffer, 0, this.message_buffer.Length);

            this.current_position = 0;
            this.message_size = 0;
        }
    }
}
