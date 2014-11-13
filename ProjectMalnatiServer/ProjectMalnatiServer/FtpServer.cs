using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Threading;



namespace ProjectMalnatiServer
{
   
        public class FtpServer
        {
            private TcpListener _listener;

            private ClientConnection connection;

            public FtpServer()
            {
            }

            //public void Start(IPAddress ipClient)
            public void Start()

            {
                //_listener = new TcpListener(ipClient, 21);
                try
                {
                    _listener = new TcpListener(IPAddress.Any, 21);
                    _listener.Start();
                    _listener.BeginAcceptTcpClient(HandleAcceptTcpClient, _listener);
                }
                catch(Exception)
                {
                    if (_listener != null)
                        _listener.Stop();
                }    
            }

            public void Stop()
            {
                if (_listener != null)
                {
                    _listener.Stop();
                }
            }

            private void HandleAcceptTcpClient(IAsyncResult result)
            {
                //_listener.BeginAcceptTcpClient(HandleAcceptTcpClient, _listener);
                TcpClient client=null;
                try
                {
                    client = _listener.EndAcceptTcpClient(result);
                    _listener.Stop();
                    Console.WriteLine("connessione impostata con il client: " + client.Client.LocalEndPoint.ToString());
                    connection = new ClientConnection(client);
                    ThreadPool.QueueUserWorkItem(connection.HandleClient, client);
                }
               catch(Exception)
                {
                    if (client != null)
                        if (client.Connected == true)
                        client.Close(); 
                }
            }

            public void disconnectClientConnection()
            {
                connection.Disconnetti();
            }
        }
}
