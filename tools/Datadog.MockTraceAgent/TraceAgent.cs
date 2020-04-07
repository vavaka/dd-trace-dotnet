using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using MessagePack;

namespace Datadog.MockTraceAgent
{
    public class TraceAgent : IDisposable
    {
        private HttpListener _listener;
        private Thread _listenerThread;

        public int Start(int port = 8126, int retries = 5)
        {
            // try up to 5 consecutive ports before giving up
            while (true)
            {
                // seems like we can't reuse a listener if it fails to start,
                // so create a new listener each time we retry
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");

                try
                {
                    listener.Start();

                    // successfully listening
                    _listener = listener;

                    _listenerThread = new Thread(HandleHttpRequests);
                    _listenerThread.Start();

                    return port;
                }
                catch (HttpListenerException) when (retries > 0)
                {
                    // only catch the exception if there are retries left
                    port++;
                    retries--;
                }

                // always close listener if exception is thrown,
                // whether it was caught or not
                listener.Close();
            }
        }

        public void Stop()
        {
            _listener.Close();
            _listener = null;
        }

        public event EventHandler<EventArgs<HttpListenerContext>> RequestReceived;

        public event EventHandler<EventArgs<IList<IList<MockSpan>>>> RequestDeserialized;

        private void HandleHttpRequests()
        {
            while (_listener.IsListening)
            {
                try
                {
                    var context = _listener.GetContext();

                    var requestReceivedHandler = RequestReceived;

                    if (requestReceivedHandler != null)
                    {
                        requestReceivedHandler(this, new EventArgs<HttpListenerContext>(context));
                    }

                    var onRequestDeserializedHandler = RequestDeserialized;

                    if (onRequestDeserializedHandler != null)
                    {
                        var traces = MessagePackSerializer.Deserialize<IList<IList<MockSpan>>>(context.Request.InputStream);
                        onRequestDeserializedHandler(this, new EventArgs<IList<IList<MockSpan>>>(traces));
                    }

                    context.Response.ContentType = "application/json";
                    var buffer = Encoding.UTF8.GetBytes("{}");
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.Close();
                }
                catch (HttpListenerException)
                {
                    // listener was stopped,
                    // ignore to let the loop end and the method return
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ((IDisposable)_listener)?.Dispose();
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}