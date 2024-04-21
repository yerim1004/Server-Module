using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace serverModule
{
    class CDoubleBufferingQueue : ILogicQueue
    {
        Queue<CPacket> queue1;
        Queue<CPacket> queue2;

        Queue<CPacket> ref_input;
        Queue<CPacket> ref_output;

        object cs_write;

        public CDoubleBufferingQueue()
        {
            //초기 세팅은 1:1로 매칭되도록 설정
            this.queue1 = new Queue<CPacket>();
            this.queue2 = new Queue<CPacket>();

            this.ref_input = this.queue1;
            this.ref_output = this.queue2;

            this.cs_write = new object();
        }

        //IO 스레드에서 전달한 패킷을 보관한다.
        void ILogicQueue.enqueue(CPacket msg)
        {
            lock (this.cs_write)
            {
                this.ref_input.Enqueue(msg);
            }
        }
        Queue<CPacket> ILogicQueue.get_all()
        {
            swap();
            return this.ref_output;
        }
        void swap()
        {
            lock (this.cs_write)
            {
                Queue<CPacket> temp = this.ref_input;
                this.ref_input = this.ref_output;
                this.ref_output = temp;
            }
        }
    }
}
