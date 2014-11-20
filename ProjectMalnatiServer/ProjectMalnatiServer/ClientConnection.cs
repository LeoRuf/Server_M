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
        private FtpServer rif;
        //atrributi connessione CONTROL
        private TcpClient _controlClient; //memorizza le info sul client
        //posso tirarne fuori le info di connessione
        //per instaurare la connessione dati su richiesta del client
        private NetworkStream _controlStream;
        private StreamReader _controlReader;
        private StreamWriter _controlWriter;

        /****************************/
        //attributi connessione DATA (server like)
        private string _transferType;
        private TcpClient _dataClient;
        private IPEndPoint _dataEndpoint;

        private TcpClient dataConnection;

        private string fileName;
        private string fileExtension;
        private bool isDir;
        /********************************/

        volatile bool _shouldStop;

        public ClientConnection(TcpClient client, FtpServer rif)
        {
            try
            {
                this.rif = rif;
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
                                case "COPY":
                                    //devo far partire il listener e dare il via al client
                                    response = Copy(arguments);
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
            if (_dataClient != null)
                //if(_dataClient.Client != null && _dataClient.Connected == true)
                // _dataClient.Close();
                if (_dataClient.Client != null)
                    _dataClient.Close();
            if (_controlClient != null)
            {
                //if (_controlClient.Connected == true)
                //    _controlClient.Close();

                _controlClient.Close();
            }

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

                _transferType = "T"; //ipotizzo di trasferire un plain text, in caso negativo lo cambio

                object plainText = null;
                object stringObject = null; // Used to store the return value
                var thread = new Thread(
                  () =>
                  {
                      IDataObject dataObject = Clipboard.GetDataObject();
                      if (Clipboard.ContainsFileDropList())
                      {
                          plainText = false;
                          StringCollection strColl = Clipboard.GetFileDropList();
                          StringEnumerator myEnumerator = strColl.GetEnumerator();
                          while (myEnumerator.MoveNext())
                          {
                              stringObject = myEnumerator.Current.ToString();
                          }
                      }
                      else
                      {                        
                              plainText = true;
                              stringObject = Clipboard.GetText();          
                      }
                  });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                long size;
                string path = null;
                string message = null;
                string textClipboard = null;

                if ((bool)plainText)
                {
                    //message = DoRetrievePlainText((string)stringObject);
                    textClipboard = (string)stringObject;
                    size = textClipboard.Length;
                    message = size + "!Text";
                }
                else 
                {
                    path = (string)stringObject;
                    message = DoRetrieveFileOrDir(ref path);
                    FileInfo fInfo = new FileInfo(path);
                    size = fInfo.Length;
                }
               

                using (NetworkStream dataStream = _dataClient.GetStream())
                {
                    //stream per le comunicaz preliminari di controllo
                    //non mandate sul canale di controllo perche' verrebbero intercettate dal thread
                    //che esegue il dispatching dei comandi al server

                    StreamReader dataCtrlStreamR = new StreamReader(dataStream);
                    StreamWriter dataCtrlStreamW = new StreamWriter(dataStream);

                    dataCtrlStreamW.WriteLine(message);
                    dataCtrlStreamW.Flush();

                    if (message.Equals("empty"))
                        throw new Exception();

                    string answer = dataCtrlStreamR.ReadLine();

                    if (_transferType.Equals("T"))
                    {
                        CopyStreamPlainText(textClipboard, dataStream);
                    }
                    else
                    {
                        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                        {
                            CopyStream(fs, dataStream, 4096);
                            //se ho inviato una cartella (compressa) devo eliminare lo zip creato
                            if (message.StartsWith("dir;"))
                            {
                                if (File.Exists(path))
                                    File.Delete(path);
                            }
                        }
                    }

                    dataCtrlStreamR.Close();
                    dataCtrlStreamW.Close();
                    dataStream.Close();
                }
                _dataClient.Close();
            }
            catch (Exception)
            {
                //Disconnetti();
                if (_dataClient != null)
                    if (_dataClient.Connected)
                        _dataClient.Close();
            }
        }

        //si occupa di informare il client che sta per inviare un file o una cartella (compressa), in tal caso la crea
        private string DoRetrieveFileOrDir(ref string path)
        {
            try
            {
                _transferType = "F";
                FileInfo fInfo = new FileInfo(path);
                fileName = fInfo.Name;
                string[] extensionArray = fileName.Split('.');

                if(File.Exists(path))
                {
                    
                    long size = fInfo.Length;
                    return size + "!" + fileName;
                }
                else 
                {
                    //modifico path con il nuovo path al direttorio compresso
                    path = CompressDir(path);
                    FileInfo modifiedfInfo = new FileInfo(path); //in questo caso dobbiamo inviare qualcosa di diverso dal contenuto della clipboard
                    fileName = modifiedfInfo.Name; //ritocco fileName con il nome della cartella compressa contenente il direttorio copiato
                    long size = modifiedfInfo.Length;
                    return size + "!dir;" + fileName; //devo informare il client che sto inviando un direttorio sotto forma di file compresso
  
                }
                
                //if (extensionArray.Length == 1) //caso di direttorio
                //{
                //    //modifico path con il nuovo path al direttorio compresso
                //    path = CompressDir(path);
                //    FileInfo modifiedfInfo = new FileInfo(path); //in questo caso dobbiamo inviare qualcosa di diverso dal contenuto della clipboard
                //    fileName = modifiedfInfo.Name; //ritocco fileName con il nome della cartella compressa contenente il direttorio copiato
                //    long size = modifiedfInfo.Length;
                //    return size + "!dir;" + fileName; //devo informare il client che sto inviando un direttorio sotto forma di file compresso
                //}
                //else // caso di file
                //{
                //    fileExtension = extensionArray[1];
                //    long size = fInfo.Length;
                //    return size + "!" + fileName;
                //}
            }
            catch (Exception)
            {
                throw;
            }
        }

        private string CompressDir(string path)
        {
            ZipFile zip = new ZipFile();
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

        private static long CopyStreamPlainText(string input, Stream output)
        {
            //byte[] bufferString;
            //char[] bufferChar;
            //int count = 0;
            long total = 0;

            try
            {
                //bufferString = Encoding.UTF8.GetBytes(input);
                //using (MemoryStream ms = new MemoryStream(bufferString))
                //{
                //    using (StreamReader streamReader = new StreamReader(ms))
                //    {
                //        using(StreamWriter streamWriter = new StreamWriter(output))
                //        {
                //            while ((count = streamReader.Read(bufferChar, 0, bufferChar.Length)) > 0)
                //            {
                //                streamWriter.Write()
                //            }
                //        }
                //    }
                //}
                using (StreamWriter streamWriter = new StreamWriter(output))
                    streamWriter.Write(input);

                return total;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /**********************************************/
        //metodi client-like

        //copia la clipboard nel server
        private string Copy(string pathname)
        {
            char[] stringBuffer = new char[4096];
            byte[] buffer = new byte[4096];
            int total = 0;
            int count = 0;
            bool ricevuto = false;
            bool isText;
            isDir = false;
            FileStream file = null;
            string filePath = null;
            bool yes=false; //usato per non far riapparire il messaggio di errore in caso di annullamento volontario copia clipboard nell'eccezione

            if(dataConnection!=null)
            {
                if (dataConnection.Connected)
                {
                  bool answer =  rif.ShowOptions();
                    if (answer == true)
                    {
                        yes = true;
                        throw new Exception();
                    }
                     return "copia clipboard interrotta";
                }
            }

            TcpListener dataListener = null;
            dataConnection = null;
            try
            {
                //in ascolto sulla stessa porta sulla quale ascolta il client
                //quando e' il server a copiare la sua clipboard
                dataListener = new TcpListener(IPAddress.Any, _dataEndpoint.Port);
                dataListener.Start();

                _controlWriter.WriteLine("go");
                _controlWriter.Flush();

                dataConnection = dataListener.AcceptTcpClient();
                dataListener.Stop();

                NetworkStream dataNetworkStream = dataConnection.GetStream();
                StreamReader dataStreamR = new StreamReader(dataNetworkStream);
                StreamWriter dataStreamW = new StreamWriter(dataNetworkStream);

                string fileName = dataStreamR.ReadLine(); //legge il messaggio mandato dal client
                string text = null;

                string[] sizeArray = fileName.Split('!');
                long size = Convert.ToInt64(sizeArray[0]);
                fileName = sizeArray[1];

                if (fileName.Equals("Text"))
                {
                    isText = true;
                    if (dataNetworkStream.CanWrite)
                    {
                        dataStreamW.WriteLine("go");
                        dataStreamW.Flush();
                    }
                    using (StreamReader reader = new StreamReader(dataNetworkStream))
                    {

                        text = reader.ReadToEnd();
                        if (text.Length != size) //plain text corrotto
                            throw new Exception();
                    }
                }
                else
                {
                    isText = false;
                    string[] fileNameArray = fileName.Split(';'); //; inviato solo nel caso di directory
                    string[] fileNameArray1 = null; //utilizzato per tirare fuori il nome della directory (in ricez ho nomeDir.zip)

                    if (fileNameArray.Length == 2)
                    {
                        isDir = true;
                        fileName = fileNameArray[1]; //stacco dal fileName ricevuto il "dir;"
                        fileNameArray1 = fileName.Split('.');
                    }

                    if (dataNetworkStream.CanWrite)
                    {
                        dataStreamW.WriteLine("go");
                        dataStreamW.Flush();
                    }
                    if (!Directory.Exists("C:\\tempServer"))
                        Directory.CreateDirectory("C:\\tempServer");

                    filePath = "C:\\tempServer\\" + fileName; //path al file di destinazione in temp (file o cartella comp)
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                    file = File.Create(filePath);

                    //fase di lettura indipendente dal tipo di file (i direttori sono letti e memorizzati come files compressi)
                    using (BinaryReader bReader = new BinaryReader(dataNetworkStream))
                    {
                        using (BinaryWriter bWriter = new BinaryWriter(file))
                        {
                            while ((count = bReader.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                bWriter.Write(buffer, 0, count);
                                total += count;
                            }
                        }
                    }
                    file.Close();
                    FileInfo fInfo = new FileInfo(filePath);
                    if (fInfo.Length != size)
                        throw new Exception(); //file corrotto

                    if (isDir)
                    {
                        string nome_dir = null;
                        for (int i = 0; i < fileNameArray1.Length - 1; i++)
                        {

                            if (nome_dir == null)
                            {
                                nome_dir = nome_dir + fileNameArray1[i];
                                continue;
                            }
                            nome_dir = nome_dir + "." + fileNameArray1[i];
                        }

                        using (ZipFile zip = ZipFile.Read(filePath))
                        {
                            if (Directory.Exists("C:\\tempServer\\" + nome_dir))
                                DeleteDirectory("C:\\tempServer\\" + nome_dir);
                            Directory.CreateDirectory("C:\\tempServer\\" + nome_dir);
                            foreach (ZipEntry e in zip)
                            {
                                e.Extract("C:\\tempServer\\" + fileNameArray1[0]);
                            }
                        }
                        if (File.Exists(filePath)) //elimino il file compresso ricevuto, ora ho la cartella decompressa
                            File.Delete(filePath);
                        filePath = "C:\\tempServer\\" + fileNameArray1[0]; //imposto il nome del path alla cartella decompressa
                    }
                    //Console.WriteLine("File ricevuto!");    
                    ricevuto = true;
                }
                dataStreamR.Close();
                dataConnection.Close(); //connessione dati abbattuta dopo ogni trasferimento

                if (isText)
                    rif.SetClipText(text);
                else
                {
                    StringCollection s = new StringCollection();
                    s.Add(filePath);
                    rif.setClip(s);
                }
                return "Copia della clipboard dal client al server";

            }

            catch (Exception)
            {
                if (file != null)
                    file.Close();
                if (ricevuto == false && yes==false) //per il plain text non devo fare niente, al rientro dalla funzione la stringa corrotta e' eliminata senza essere copiata nella clipboard
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    rif.DisplayErrorMessage();
                }
                if (dataListener != null)
                    dataListener.Stop();
                if (dataConnection != null)
                    if (dataConnection.Connected)
                        dataConnection.Close();

                return "Copia della clipboard dal client al server";
            }
        }

        public static void DeleteDirectory(string target_dir)
        {
            string[] files = Directory.GetFiles(target_dir);
            string[] dirs = Directory.GetDirectories(target_dir);

            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in dirs)
            {
                DeleteDirectory(dir);
            }

            Directory.Delete(target_dir, false);
        }
        #endregion
    }
}
