using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace serverModule
{
    public interface IMessageDispatcher
    {
        void on_message(CUserToken user, ArraySegment<byte> buffer);
    }
}
