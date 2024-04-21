using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;

namespace serverModule
{
    public class CUserToken
    {
        enum State
        {
            Idle,

            Connected,

            //종료 예약
            ReserveClosing,

            Closed,
        }

        //종료 요청 서버 -> 클라
        const short SYS_CLOSE_REQ = 0;
        //종료 응답 클라 -> 서버
        const short SYS_CLOSE_ACK = -1;
        //하트비트 시작 서버 -> 클라
        public const short SYS_START_HEARTBEAT = -2;
        //하트비트 갱신 클라 -> 서버
        public const short SYS_UPDATE_HEARTBEAT = -3;

        //close 중복 처리 방지. 0 = 연결된 상태, 1 = 종료된 상태
        int is_closed;

        State current_state;
        public Socket socket { get; set; }

        public SocketAsyncEventArgs receive_event_args { get; private set; }
        public SocketAsyncEventArgs send_event_args { get; private set; }

        CMessageResolver message_resolver;

        IPeer peer;

        List<ArraySegment<byte>> sending_list;
        private object cs_sending_queue;

        IMessageDispatcher dispatcher;

        public delegate void ClosedDelegate(CUserToken token);
        public ClosedDelegate on_session_closed;

        public long latest_heartbeat_time { get; private set; }
        CHeartbeatSender heartbeat_sender;
        bool auto_heartbeat;

        public CUserToken(IMessageDispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
            this.cs_sending_queue = new object();

            this.message_resolver = new CMessageResolver();
            this.peer = null;
            this.sending_list = new List<ArraySegment<byte>>();
            this.latest_heartbeat_time = DateTime.Now.Ticks;

            this.current_state = State.Idle;
        }
        public void on_receive(byte[] buffer, int offset, int transfered)
        {
            this.message_resolver.on_receive(buffer, offset, transfered, on_message_completed);
        }
        public void on_connected()
        {
            this.current_state = State.Connected;
            this.is_closed = 0;
            this.auto_heartbeat = true;
        }
        public void set_peer(IPeer peer)
        {
            this.peer = peer;
        }
        public void set_event_args(SocketAsyncEventArgs receive_event_args, SocketAsyncEventArgs send_event_args)
        {
            this.receive_event_args = receive_event_args;
            this.send_event_args = send_event_args;
        }
        void on_message_completed(ArraySegment<byte> buffer)
        {
            if (this.peer == null)
            {
                return;
            }

            if (this.dispatcher != null)
            {
                //로직 스레드
                this.dispatcher.on_message(this, buffer);
            }
            else
            {
                //IO스레드
                CPacket msg = new CPacket(buffer, this);
                on_message(msg);
            }
        }
        public void on_message(CPacket msg)
        {
            //서버에서 종료 연락이 왔는지 체크한다.
            //종료 신호를 받았다면 disconnect를 호출, 받은 쪽에서 먼저 종료 요청을 보낸다.
            switch (msg.protocol_id)
            {
                case SYS_CLOSE_REQ:
                    disconnect();
                    return;

                case SYS_START_HEARTBEAT:
                    {
                        msg.pop_protocol_id();

                        byte interval = msg.pop_byte();
                        this.heartbeat_sender = new CHeartbeatSender(this, interval);

                        if (this.auto_heartbeat)
                        {
                            start_heartbeat();
                        }
                    }
                    return;

                case SYS_UPDATE_HEARTBEAT:
                    this.latest_heartbeat_time = DateTime.Now.Ticks;
                    return;
            }

            if(this.peer != null)
            {
                try
                {
                    switch (msg.protocol_id)
                    {
                        case SYS_CLOSE_ACK:
                            this.peer.on_removed();
                            break;

                        default:
                            this.peer.on_message(msg);
                            break;
                    }
                }
                catch (Exception)
                {
                    close();
                }
            }

            if (msg.protocol_id == SYS_CLOSE_ACK)
            {
                if (this.on_session_closed != null)
                {
                    this.on_session_closed(this);
                }
            }
        }
        public void close()
        {
            //중복 수행을 막는다.
            if(Interlocked.CompareExchange(ref this.is_closed, 1, 0) == 1)
            {
                return;
            }

            if (this.current_state == State.Closed)
            {
                return;
            }

            this.current_state = State.Closed;
            this.socket.Close();
            this.socket = null;

            this.send_event_args.UserToken = null;
            this.receive_event_args.UserToken = null;

            this.sending_list.Clear();
            this.message_resolver.clear_buffer();

            if (this.peer != null)
            {
                CPacket msg = CPacket.create((short)-1);
                if(this.dispatcher!= null)
                {
                    this.dispatcher.on_message(this, new ArraySegment<byte>(msg.buffer, 0, msg.position));
                }
                else
                {
                    on_message(msg);
                }
            }
        }

        //패킷을 전송하는 메소드
        public void send(ArraySegment<byte> data)
        {
            lock (this.cs_sending_queue)
            {
                this.sending_list.Add(data);

                if (this.sending_list.Count > 1)
                {
                    return;
                }
            }

            start_send();
        }
        public void send(CPacket msg)
        {
            //CPacket clone = new CPacket();

            //msg.copy_to(clone);

            //lock (this.cs_sending_queue)
            //{
            //    if (this.sending_queue.Count <= 0)
            //    {
            //        this.sending_queue.Enqueue(msg);
            //        start_send();
            //        return;
            //    }

            //    this.sending_queue.Enqueue(msg);
            //}
            msg.record_size();
            send(new ArraySegment<byte>(msg.buffer, 0, msg.position));
        }
        void start_send()
        {
            try
            {
                this.send_event_args.BufferList = this.sending_list;

                bool pending = this.socket.SendAsync(this.send_event_args);
                if (!pending)
                {
                    process_send(this.send_event_args);
                }
            }
            catch(Exception e)
            {
                if(this.socket == null)
                {
                    close();
                    return;
                }

                Console.WriteLine("send error!! close socket." + e.Message);
                throw new Exception(e.Message, e);
            }
        }

        static int sent_count = 0;
        static object cs_count = new object();

        //비동기 전송 완료시 호출되는 콜백 메소드.
        public void process_send(SocketAsyncEventArgs e)
        {
            if(e.BytesTransferred <= 0 || e.SocketError != SocketError.Success)
            {
                //연결이 끊겨 이미 소켓이 종료된 경우일 것.
                return;
            }

            lock (this.cs_sending_queue)
            {
                var size = this.sending_list.Sum(obj => obj.Count);

                if(e.BytesTransferred != size)
                {
                    if(e.BytesTransferred < this.sending_list[0].Count)
                    {
                        string error = string.Format("Need to send more. transferred {0}, packet size {1}", e.BytesTransferred, size);
                        Console.WriteLine(error);

                        close();
                        return;
                    }

                    int sent_index = 0;
                    int sum = 0;
                    //이미 전송된 데이터를 빼고 대기중인 데이터를 보낸다.
                    for(int i=0; i < this.sending_list.Count; ++i)
                    {
                        sum += this.sending_list[i].Count;
                        if(sum <= e.BytesTransferred)
                        {
                            //여기까지 전송 완료된 데이터의 인덱스.
                            sent_index = i;
                            continue;
                        }

                        break;
                    }
                    this.sending_list.RemoveRange(0, sent_index + 1);

                    start_send();
                    return;
                }

                this.sending_list.Clear();

                if (this.current_state == State.ReserveClosing)
                {
                    this.socket.Shutdown(SocketShutdown.Send);
                }
            }
        }

        //연결을 종료한다. 주로 클라이언트에서 종료할 때 호출.
        public void disconnect()
        {
            try
            {
                if(this.sending_list.Count <= 0)
                {
                    this.socket.Shutdown(SocketShutdown.Send);
                    return;
                }
                this.current_state = State.ReserveClosing;
            }
            catch (Exception)
            {
                close();
            }
        }

        //연결을 종료. 단, 종료코드를 전송한 뒤 상대방이 연결을 끊게 한다.
        //주로 서버에서 클라이언트의 연결을 끊을 때 사용한다.
        //TIME_WAIT(??) 상태를 서버에 남기지 않으려면 disconnect대신 이 메소드를 사용해야한다.
        public void ban()
        {
            try
            {
                byebye();
            }
            catch (Exception)
            {
                close();
            }
        }

        void byebye()
        {
            CPacket bye = CPacket.create(SYS_CLOSE_REQ);
            send(bye);
        }
        public bool is_connected()
        {
            return this.current_state == State.Connected;
        }
        public void start_heartbeat()
        {
            if(this.heartbeat_sender != null)
            {
                this.heartbeat_sender.play();
            }
        }
        public void stop_heartbeat()
        {
            if(this.heartbeat_sender != null)
            {
                this.heartbeat_sender.stop();
            }
        }
        public void disable_auto_heartbeat()
        {
            stop_heartbeat();
            this.auto_heartbeat = false;
        }
        public void update_heartbeat_manually(float time)
        {
            if(this.heartbeat_sender != null)
            {
                this.heartbeat_sender.update(time);
            }
        }
    }
}
