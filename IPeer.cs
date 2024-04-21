using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;

namespace serverModule
{

    //서버와 클라이언트 공통으로 사용하는 세션 객체
    //서버 : 
    //       하나의 클라이언트 객체를 나타냄.
    //       이 인터페이스를 구현한 객체를 CNetworkService의 session_created_callback 호출시 생성
    //       객체 풀링 여부는 사용자 원하는 대로

    //클라이언트 :
    //            접속한 서버 객체를 나타냄
    public interface IPeer
    {
        ///CNetworkService.initialize에서 use_logicthread를 true로 설정할 경우
        /// -> IO스레드에서 직접 호출됨.
        /// 
        /// false로 설정할 경우
        /// -> 로직 스레드에서 호출됨. 로직 스레드는 싱글 스레드로 돌아감.
        void on_message(CPacket msg);


        void on_removed();

        void send(CPacket msg);

        void disconnect();
    }
}
