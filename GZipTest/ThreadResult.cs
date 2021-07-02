using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GZipTest
{
    public class ThreadResult
    {
        public ThreadResult()
        {
            WaitHandle = new ManualResetEvent(false);
        }

        public ManualResetEvent WaitHandle { get; }

        public Exception Exception { get; set; }

        public bool Success => Exception == null;
    }
}
