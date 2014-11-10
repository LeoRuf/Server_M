using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Net.Sockets;
using System.Net;
//using log4net;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Drawing;
using System.Threading;

namespace ProjectMalnatiServer
{
    public class ClientConnection
    {
        //atrributi connessione CONTROL
        private TcpClient _controlClient; //memorizza le info sul client
        //posso tirarne fuori le info di connessione
        //per instaurare la connessione dati su richiesta del client
        private NetworkStream _controlStream;
        private StreamReader _controlReader;
        private StreamWriter _controlWriter;


        /****************************/
        //attributi connessione DATA
        private string _transferType;
        private TcpClient _dataClient;
        private IPEndPoint _dataEndpoint;
        /********************************/

        private string _username;

        public ClientConnection(TcpClient client)
        {
            _controlClient = client;

            _controlStream = _controlClient.GetStream();

            _controlReader = new StreamReader(_controlStream);
            _controlWriter = new StreamWriter(_controlStream);
        }

        public void HandleClient(object obj)
        {
            _controlWriter.WriteLine("220 Service Ready.");
            _controlWriter.Flush();

            string line;

            try
            {
                //continua a ricevere comandi dal client
                while (true)
                {
                    while (!string.IsNullOrEmpty(line = _controlReader.ReadLine()))
                    {
                        //Console.WriteLine("sono entrato nel while\n");
                        string response = null;

                        //separo i pezzi di comando 
                        string[] command = line.Split(' ');

                        string cmd = command[0].ToUpperInvariant(); //maiuscole e minuscole cosi' non fanno differenza
                        string arguments = command.Length > 1 ? line.Substring(command[0].Length + 1) : null;

                        if (string.IsNullOrWhiteSpace(arguments))
                            arguments = null;

                        if (response == null)
                        {
                            switch (cmd) //possibili comandi inviati via dal client sul canale di controllo, porta 21
                            {

                                //case "USER": //inutile
                                //    response = User(arguments);
                                //    break;
                                //case "PASS": //inutile
                                //    response = Password(arguments);
                                //    break;
                                //case "CWD": //inutile
                                //    response = ChangeWorkingDirectory(arguments);
                                //    break;
                                //case "CDUP": //inutilr
                                //    response = ChangeWorkingDirectory("..");
                                //    break;
                                //case "PWD": //inutile
                                //    response = "257 \"/\" is current directory.";
                                //    break;
                                case "QUIT":
                                    response = "221 Service closing control connection";
                                    break;
                                //case "TYPE": //tipo di file che il client vuole scaricare
                                //    string[] splitArgs = arguments.Split(' ');
                                //    response = Type(splitArgs[0], splitArgs.Length > 1 ? splitArgs[1] : null);
                                //    break;
                                case "PORTA": //connessione attiva, non c'e' da attraversare un firewall o NAT
                                    response = Port(arguments);
                                    break;
                                case "RETR": //per scaricare dal server dei files
                                    response = Retrieve(arguments);
                                    break;

                                default:
                                    response = "502 Command not implemented";
                                    break;
                            }
                        }

                        if (_controlClient == null || !_controlClient.Connected)
                        {
                            break; //se si chiama il metodo stop di tcpclient
                        }
                        else
                        {
                            _controlWriter.WriteLine(response);
                            _controlWriter.Flush(); //flush spedisce lo stream accumulato

                            if (response.StartsWith("221"))
                            {
                                break; //se il client chiama quit
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }

        #region FTP Commands

        ////USER
        //private string User(string username) 
        //{
        //    _username = username;

        //    return "331 Username ok, need password";
        //}

        ////PASS
        //private string Password(string password)
        //{
        //    if (true)
        //    {
        //        return "230 User logged in";
        //    }
        //    else
        //    {
        //        return "530 Not logged in";
        //    }
        //}

        ////CWD
        //private string ChangeWorkingDirectory(string pathname)
        //{
        //    return "250 Changed to new directory";
        //}

        //TYPE
        //private string Type(string typeCode, string formatControl)
        //{
        //    string response = "500 ERROR";

        //    switch (typeCode)
        //    {
        //        case "A":
        //            _transferType = typeCode;
        //            response = "200 OK, ASCII file";
        //            break;
        //        case "I":
        //            _transferType = typeCode;
        //            response = "200 OK, Image file";
        //            break;
        //        case "E":
        //        case "L":
        //        default:
        //            response = "504 Command not implemented for that parameter.";
        //            break;
        //    }

        //    if (formatControl != null)
        //    {
        //        switch (formatControl)
        //        {
        //            case "N":
        //                response = "200 OK";
        //                break;
        //            case "T":
        //            case "C":
        //            default:
        //                response = "504 Command not implemented for that parameter.";
        //                break;
        //        }
        //    }

        //    return response;
        //}

        //PORT: il comando porta permette al client di scegliere una porta da usare per il
        //canale DATA
        private string Port(string host)
        {
            IPEndPoint ipep = (IPEndPoint)_controlClient.Client.RemoteEndPoint;
            IPAddress ipa = ipep.Address;

            /*******************************/
            //if (BitConverter.IsLittleEndian)
            //    Array.Reverse(bytePortArray);

            //short port=BitConverter.ToInt16(bytePortArray, 0);
            /*********************************************/

            //_dataEndpoint = new IPEndPoint(((IPEndPoint)_controlClient.Client.RemoteEndPoint).Address, 20);
            //_dataEndpoint = new IPEndPoint(((IPEndPoint)_controlClient.Client.RemoteEndPoint).Address, port);

            short portshort = Convert.ToInt16(host);
            _dataEndpoint = new IPEndPoint(ipa, portshort);

            string response = "Il server e' pronto ad inviare files";
            return response;
        }

        private string Retrieve(string pathname)
        {
            //in realta' qui' non devo inviare il file da una cartella ma 
            //devo inviare un file il cui contenuto rispecchia la clipboard
            //pathname qui' inutile, non devo specificare una directory scelta dal client

            _dataClient = new TcpClient(); //creo la connessione 
            _dataClient.BeginConnect(_dataEndpoint.Address, _dataEndpoint.Port, DoRetrieve, pathname);
            return "Inizia il tentativo di connessione al client\n";
        }

        private void DoRetrieve(IAsyncResult result)
        {
            //provo ad inviare un file predefinito, nella cartella dei files delle classi di VS
            //Console.WriteLine("Connessione dati stabilita\n");

            _dataClient.EndConnect(result); //modalita' attiva
            //NetworkStream dataStream = _dataClient.GetStream();
            //StreamReader clientReader = new StreamReader(dataStream);
            //string risposta = clientReader.ReadLine();
            //Console.WriteLine("Risposta: " + risposta);
            //StreamWriter clientWriter = new StreamWriter(dataStream);
            //clientWriter.WriteLine("daje");
            //clientWriter.Flush();

            //clipboard operations
            /*
             * 
             * 
             * 
             * *
             * *
             * *
             */
            ////


            //object fi=null;

            object pathObject = null; // Used to store the return value
            var thread = new Thread(
              () =>
              {
                  IDataObject dataObject = Clipboard.GetDataObject();
                  if (Clipboard.ContainsFileDropList())
                  {
                      StringCollection strColl = Clipboard.GetFileDropList();
                      StringEnumerator myEnumerator = strColl.GetEnumerator();
                      while (myEnumerator.MoveNext())
                      {
                          //Console.WriteLine("   {0}", myEnumerator.Current);
                          pathObject = myEnumerator.Current.ToString();
                      }
                      //Console.WriteLine();
                      //fi = new FileInfo(path);
                      //string fileName = fi.Name;
                      //string[] extensionArray = fileName.Split('.');
                      //string extension = extensionArray[1];

                      //switch (extension)
                      //{
                      //    case "txt":
                      //        break;
                      //    case "wav":
                      //        break;
                      //    default: //immagini
                      //        break;
                      //}
                  }
              });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            string path;
            FileInfo fInfo;
            string fileName;
            string[] extensionArray = null;
            string extension = null;
            if (pathObject != null)
            {
                path = (string)pathObject;
                fInfo = new FileInfo(path);
                fileName = fInfo.Name;
                extensionArray = fileName.Split('.');
                extension = extensionArray[1];

                using (NetworkStream dataStream = _dataClient.GetStream())
                {
                    //apre il file di prova nel direttorio corrente
                    //using (FileStream fs = new FileStream("prova.txt", FileMode.Open, FileAccess.Read))
                    //using (FileStream fs = new FileStream("prova.txt", FileMode.Open, FileAccess.Read))
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        CopyStream(fs, dataStream);
                    }
                }
            }

            ////



            ////bisogna capire dove vanno messi
            //_dataClient.Close();
            //_dataClient = null;

            //informazioni di controllo mandate al client sul canale di controllo
            //_controlWriter.WriteLine("226 Closing data connection, file transfer successful");
            //_controlWriter.Flush();
        }

        //private void GetClipboardData(Action<Object> OnFinished)
        //{
        //    Thread t = new Thread(() =>
        //    {
        //        object data = Clipboard.GetData(DataFormats.Serializable);
        //        OnFinished(data);
        //    });
        //    t.SetApartmentState(ApartmentState.STA);
        //    t.Start();
        //}

        private static long CopyStream(Stream input, Stream output, int bufferSize)
        {
            byte[] buffer = new byte[bufferSize];
            int count = 0;
            long total = 0;

            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, count);
                total += count;
            }

            return total;
        }

        private static long CopyStreamAscii(Stream input, Stream output, int bufferSize)
        {
            char[] buffer = new char[bufferSize];
            int count = 0;
            //long total = 0;
            long total = 0;

            using (StreamReader rdr = new StreamReader(input))
            {
                using (StreamWriter wtr = new StreamWriter(output, Encoding.ASCII))
                {
                    while ((count = rdr.Read(buffer, 0, buffer.Length)) > 0)
                    //while ((count = rdr.Read(buffer, total, buffer.Length)) > 0)
                    {
                        //wtr.Write(buffer, 0, count);
                        wtr.Write(buffer, 0, count);
                        //Console.WriteLine(buffer);
                        //wtr.Flush();
                        total += count;
                    }
                    wtr.Flush();
                    wtr.Close();
                    //while (!string.IsNullOrEmpty(buffer = rdr.ReadLine()))
                    //{
                    //    wtr.WriteLine(buffer);
                    //    wtr.Flush();
                    //}
                }

            }

            output.Close();
            return total;
        }

        private long CopyStream(Stream input, Stream output)
        {
            //if (_transferType == "I")
            //{
            //    return CopyStream(input, output, 4096);
            //}
            //else
            //{
            //    return CopyStreamAscii(input, output, 4096);
            //}
            return CopyStreamAscii(input, output, 4096);
        }

        #endregion
    }
}
