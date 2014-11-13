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
using System.IO.Compression;
using Ionic.Zip;

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

        private string fileName;
        private string fileExtension;
        long size;
        /********************************/

        volatile bool _shouldStop;

        public ClientConnection(TcpClient client)
        {
            try
            {
                _controlClient = client;

                _controlStream = _controlClient.GetStream();

                _controlReader = new StreamReader(_controlStream);
                _controlWriter = new StreamWriter(_controlStream);
            }
            catch (Exception)
            {
                Disconnetti();
            }
        }

        public void HandleClient(object obj)
        {
            _controlWriter.WriteLine("220 Service Ready.");
            _controlWriter.Flush();

            _shouldStop = false;
            string line;
            try
            {
                //continua a ricevere comandi dal client
                while (!_shouldStop)
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
                                case "QUIT":
                                    response = "221 Service closing control connection";
                                    break;
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
                                _shouldStop = true;
                                Disconnetti();
                                break; //se il client chiama quit
                            }
                        }
                    }
                    //if (_controlClient == null || !_controlClient.Connected)
                    //{
                    //    break; //se si chiama il metodo stop di tcpclient
                    //}
                }
            }
            catch (Exception ex)
            {
                //l'eccezione e' scatenata solo dall'abbattimento brusco della connessione da parte del server
                //quando e' il client a chiudere la connessione si passa mediante il metodo quit, i cui risultati
                //sono uguali a quelli provocati dal blocco catch (abbattimento di tutte le connessioni)
                Console.WriteLine(ex);
                Disconnetti();
                //throw;
            }
        }
        public void Disconnetti()
        {
            //chiudendo lo stream, il metodo handleClient va in eccezione, uscendo dai due cicli while
            if (_controlStream != null)
                _controlStream.Close();
            if (_dataClient != null && _dataClient.Client != null && _dataClient.Connected == true)
                _dataClient.Close();
            if (_controlClient != null && _controlClient.Connected == true)
                _controlClient.Close();
        }

        #region FTP Commands

        //PORT: il comando porta permette al client di scegliere una porta da usare per il
        //canale DATA
        private string Port(string host)
        {
            IPEndPoint ipep = (IPEndPoint)_controlClient.Client.RemoteEndPoint;
            IPAddress ipa = ipep.Address;

            short portshort = Convert.ToInt16(host);
            _dataEndpoint = new IPEndPoint(ipa, portshort);

            string response = "Il server e' pronto ad inviare files";
            return response;
        }

        private string Retrieve(string pathname)
        {
            try
            {
                _dataClient = new TcpClient(); //creo la connessione 
                _dataClient.BeginConnect(_dataEndpoint.Address, _dataEndpoint.Port, DoRetrieve, pathname);
                return "Inizia il tentativo di connessione al client\n";
            }
            catch (Exception)
            {
                Disconnetti();
                return "Connessione ftp caduta!!!";
            }
        }

        private void DoRetrieve(IAsyncResult result)
        {
            try
            {
                _dataClient.EndConnect(result); //modalita' attiva
                _transferType = "T";
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
                      }
                  });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                string path;
                FileInfo fInfo;
                string[] extensionArray = null;
                

                if (pathObject != null)
                {
                    _transferType = "F"; 
                    path = (string)pathObject;
                    fInfo = new FileInfo(path);
                    fileName = fInfo.Name;
                    extensionArray = fileName.Split('.');
                    string dirOrFile = null;
                    if (extensionArray.Length == 1)
                    {
                        path = CompressAndSend(path);
                        FileInfo modifiedfInfo = new FileInfo(path);
                        size = modifiedfInfo.Length;
                        Console.WriteLine("Dimensione file " +modifiedfInfo.Length);
                        fileName = modifiedfInfo.Name;
                        dirOrFile = "dir;" + fileName;
                    }
                    else
                    {
                        fileExtension = extensionArray[1];
                        dirOrFile = fileName;
                        size = fInfo.Length;
                    }
                    
                    using (NetworkStream dataStream = _dataClient.GetStream())
                    {
                        //stream per le comunicaz preliminari di controllo
                        //non mandate sul canale di controllo perche' verrebbero intercettate dal thread
                        //che esegue il dispatching dei comandi al server

                        StreamReader dataCtrlStreamR = new StreamReader(dataStream);
                        StreamWriter dataCtrlStreamW = new StreamWriter(dataStream);

                        dataCtrlStreamW.WriteLine(dirOrFile);
                        dataCtrlStreamW.Flush();

                        string answer = dataCtrlStreamR.ReadLine();
                        dataCtrlStreamW.WriteLine(size);
                        dataCtrlStreamW.Flush();
                        answer = dataCtrlStreamR.ReadLine();

                        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                        {
                            CopyStream(fs, dataStream);
                        }
                        dataCtrlStreamR.Close();
                        dataCtrlStreamW.Close();
                        dataStream.Close();
                    }

                }
                else
                {
                    //devo inviare plain text e non un file
                }
                _dataClient.Close();
                //_dataClient = null;
            }
            catch (Exception)
            {
                Disconnetti();
            }
        }
        private string CompressAndSend(string path)
        {
            //string[] MainDirs = Directory.GetDirectories(path);

            //for (int i = 0; i < MainDirs.Length; i++)
            //{
            //    using (ZipFile zip = new ZipFile())
            //    {
            //        zip.UseUnicodeAsNecessary = true;
            //        zip.AddDirectory(MainDirs[i]);
            //        zip.CompressionLevel = Ionic.Zlib.CompressionLevel.BestCompression;
            //        zip.Comment = "This zip was created at " + System.DateTime.Now.ToString("G");
            //        zip.Save(string.Format("test{0}.zip", i));
            //    }
            //}
            ZipFile zip = new ZipFile();
            //zip.AddFile(path);
            zip.AddDirectory(path);
            string fileNameZip = path + ".zip";
            zip.Save(string.Format(fileNameZip));
            return fileNameZip;
        }
        
        private static long CopyStream(Stream input, Stream output, int bufferSize)
        {
            //il metodo permette di copiare qualsiasi tipo di file, lavorando in binario
            //il client sa riconoscere il tipo di file corretto grazie al messaggio inviatogli dal server con il nome del file
            byte[] buffer = new byte[bufferSize];
            int count = 0;
            long total = 0;
            try
            {
                //provo ad associare lo stream input (filestream) ad un binary reader
                using (BinaryReader bReader = new BinaryReader(input))
                {
                    //ed lo stream di output (network stream) ad un binary writer
                    using (BinaryWriter bWriter = new BinaryWriter(output))
                    {
                        while ((count = bReader.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            bWriter.Write(buffer, 0, count);
                            total += count;
                        }
                    }
                }

            }
            catch (Exception)
            {
                throw;
            }

            //try
            //{
            //    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            //    {
            //        output.Write(buffer, 0, count);
            //        total += count;
            //    }
            //}
            //catch(Exception)
            //{
            //    throw;
            //}

            return total;
        }

        private static long CopyStreamAscii(Stream input, Stream output, int bufferSize)
        {
            char[] buffer = new char[bufferSize];
            int count = 0;
            long total = 0;

            try
            {
                using (StreamReader rdr = new StreamReader(input))
                {
                    using (StreamWriter wtr = new StreamWriter(output, Encoding.ASCII))
                    {
                        while ((count = rdr.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            wtr.Write(buffer, 0, count);
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
                //output.Close();
                return total;
            }
            catch (Exception)
            {
                throw;
            }
        }

        private long CopyStream(Stream input, Stream output)
        {
            try
            {
                if (_transferType != "T") //non devo inviare plain text ma file   
                    return CopyStream(input, output, 4096);

                return CopyStreamAscii(input, output, 4096);
            }
            catch (Exception)
            {
                throw;
            }
        }

        #endregion
    }
}
