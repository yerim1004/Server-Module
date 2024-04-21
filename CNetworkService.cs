﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;


namespace serverModule
{
    public class CNetworkService
    {
        //CListener client_listener;

        SocketAsyncEventArgsPool receive_event_args_pool;
        SocketAsyncEventArgsPool send_event_args_pool;

        //BufferManager buffer_manager;

        public delegate void SessionHandler(CUserToken token);
        public SessionHandler session_created_callback { get; set; }

        public CLogicMessageEntry logic_entry { get; private set; }
        public CServerUserManager usermanager { get; private set; }

        public CNetworkService(bool use_logicthread = false)
        {
            this.session_created_callback = null;
            this.usermanager = new CServerUserManager();

            if (use_logicthread)
            {
                this.logic_entry = new CLogicMessageEntry(this);
                this.logic_entry.start();
            }
        }

        public void initialize()
        {
            int max_connections = 10000;
            int buffer_size = 1024;
            initialize(max_connections, buffer_size);
        }

        public void initialize(int max_connections, int buffer_size)
        {
            int pre_alloc_count = 1;

            BufferManager buffer_manager = new BufferManager(max_connections * buffer_size * pre_alloc_count, buffer_size);
            this.receive_event_args_pool = new SocketAsyncEventArgsPool(max_connections);
            this.send_event_args_pool = new SocketAsyncEventArgsPool(max_connections);

            buffer_manager.InitBuffer();

            SocketAsyncEventArgs arg;

            for(int i=0; i<max_connections; i++)
            {
                {
                    arg = new SocketAsyncEventArgs();
                    arg.Completed += new EventHandler<SocketAsyncEventArgs>(receive_completed);
                    arg.UserToken = null;

                    buffer_manager.SetBuffer(arg);

                    this.receive_event_args_pool.Push(arg);
                }
                //send pool
                {
                    arg = new SocketAsyncEventArgs();
                    arg.Completed += new EventHandler<SocketAsyncEventArgs>(send_completed);
                    arg.UserToken = null;

                    arg.SetBuffer(null, 0, 0);

                    this.send_event_args_pool.Push(arg);
                }
            }
        }
        public void listen(string host, int port, int backlog)
        {
            CListener client_listener = new CListener();
            client_listener.callback_on_newclient += on_new_client;
            client_listener.start(host, port, backlog);

            //heartbeat
            byte check_interval = 10;
            this.usermanager.start_heartbeat_checking(check_interval, check_interval);
        }
        public void disable_heartbeat()
        {
            this.usermanager.stop_heartbeat_checking();
        }
        public void on_connect_completed(Socket socket, CUserToken token)
        {
            token.on_session_closed += this.on_session_closed;
            this.usermanager.add(token);

            SocketAsyncEventArgs receive_event_arg = new SocketAsyncEventArgs();
            receive_event_arg.Completed += new EventHandler<SocketAsyncEventArgs>(receive_completed);
            receive_event_arg.UserToken = token;
            receive_event_arg.SetBuffer(new byte[1024], 0, 1024);

            SocketAsyncEventArgs send_event_arg = new SocketAsyncEventArgs();
            send_event_arg.Completed += new EventHandler<SocketAsyncEventArgs>(send_completed);
            send_event_arg.UserToken = token;
            send_event_arg.SetBuffer(null, 0, 0);

            begin_receive(socket, receive_event_arg, send_event_arg);
        }
        void on_new_client(Socket client_socket, object token)
        {
            SocketAsyncEventArgs receive_args = this.receive_event_args_pool.Pop();
            SocketAsyncEventArgs send_args = this.send_event_args_pool.Pop();

            //유저토큰은 매번 새로 생성
            CUserToken user_token = new CUserToken(this.logic_entry);
            user_token.on_session_closed += this.on_session_closed;
            receive_args.UserToken = user_token;
            send_args.UserToken = user_token;

            this.usermanager.add(user_token);

            user_token.on_connected();
            if(this.session_created_callback != null)
            {
                this.session_created_callback(user_token);
            }
            begin_receive(client_socket, receive_args, send_args);

            CPacket msg = CPacket.create((short)CUserToken.SYS_START_HEARTBEAT);
            byte send_interval = 5;
            msg.push(send_interval);
            user_token.send(msg);
        }

        void begin_receive(Socket socket, SocketAsyncEventArgs receive_args, 
            SocketAsyncEventArgs send_args)
        {
            CUserToken token = receive_args.UserToken as CUserToken;
            token.set_event_args(receive_args, send_args);

            token.socket = socket;

            bool pending = socket.ReceiveAsync(receive_args);
            if (!pending)
            {
                process_receive(receive_args);
            }
        }
        void receive_completed(object sender, SocketAsyncEventArgs e)
        {
            if(e.LastOperation == SocketAsyncOperation.Receive)
            {
                process_receive(e);
                return;
            }
            throw new ArgumentException("The last operation completed on the socket was not as receive.");
        }
        void send_completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                CUserToken token = e.UserToken as CUserToken;
                token.process_send(e);
            }
            catch (Exception)
            {

            }
        }
        private void process_receive(SocketAsyncEventArgs e)
        {
            CUserToken token = e.UserToken as CUserToken;
            if(e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                token.on_receive(e.Buffer, e.Offset, e.BytesTransferred);

                bool pending = token.socket.ReceiveAsync(e);
                if (!pending)
                {
                    process_receive(e);
                }
            }
            else
            {
                try
                {
                    token.close();
                }
                catch (Exception)
                {
                    Console.WriteLine("Already closed this socket.");
                }
            }
        }
        void on_session_closed(CUserToken token)
        {
            this.usermanager.remove(token);

            if(this.receive_event_args_pool != null)
            {
                this.receive_event_args_pool.Push(token.receive_event_args);
            }

            if(this.send_event_args_pool != null)
            {
                this.send_event_args_pool.Push(token.send_event_args);
            }

            token.set_event_args(null, null);
        }
    }
}
