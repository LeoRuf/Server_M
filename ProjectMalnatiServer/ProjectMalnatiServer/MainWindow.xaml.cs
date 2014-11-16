using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using System.Net.Sockets;
using System.Net;
using System.Windows.Interop;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Threading;
using WindowsInput;
using System.Collections.Specialized;



namespace ProjectMalnatiServer
{
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// </summary>

    public partial class MainWindow : Window
    {
        bool isX = true;
        string coordX = "";
        string coordY = "";
        private const int MOUSEEVENTF_LEFTDOWN = 0x02;
        private const int MOUSEEVENTF_LEFTUP = 0x04;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x08;
        private const int MOUSEEVENTF_RIGHTUP = 0x10;

        string hostname;
        string pass;
        byte[] check = new byte[64];

        Win32.POINT p = new Win32.POINT();

        private Dispatcher dispatcher;

        Socket listener;
        Socket acceptedSocket;
        IPEndPoint myEP; //endpoint del server stesso

        //delegate void performConnection();
        //performConnection pc;
        Thread workerThreadConnection;
        Thread workerThread;

        private volatile bool _shouldStop; //per bloccare thread myReceive in maniera dolce
        private bool connesso;
        private bool client;
        private volatile bool onClosing = false;

        FtpServer ftpServer;

        public MainWindow()
        {
            InitializeComponent();

            //ottenimento dell'ip locale
            hostname = Dns.GetHostName();

            IPHostEntry ipEntry = Dns.GetHostEntry(hostname);
            IPAddress[] addresses = ipEntry.AddressList;

            Console.WriteLine("Computer Host Name = " + hostname);

            for (int i = 0; i < addresses.Length; i++)
            {
                Console.WriteLine("IP Address n.{0} = {1} ", i, addresses[i].ToString());
                if (addresses[i].ToString().Length <= 16)
                {
                    label_ip_local.Content = addresses[i].ToString();
                    textBoxIP.Text = addresses[i].ToString();
                }
            }

            //label_ip_local.Foreground = Brushes.Green;
            label_ip_local.FontSize = 18;
            label_ip_local.FontWeight = FontWeights.Medium;
            listeningTextBlock.Text = "";

            //assegno coordinate fittizie a p per inizializzarlo
            p.x = 500;
            p.y = 200;

            connesso = false;

            dispatcher = Dispatcher.CurrentDispatcher;

            //da aggiornare!
            //myEP = new IPEndPoint(IPAddress.Parse(this.textBoxIP.Text), Convert.ToInt16(this.textBoxPort.Text));

            //pc = new performConnection(() =>
            //{
            //    if (connesso == true)
            //    {
            //        this.listeningTextBlock.Text = "";
            //        Console.WriteLine("Server connesso" + "\n");
            //        this.buttonListen.Content = "Disconnect";
            //        this.buttonListen.IsEnabled = true; //il bottone ora permette la disconnessione dal client
            //        this.WindowState = WindowState.Minimized;
            //        //this.Hide();
            //        //workerThreadConnection.Join(); //aspetto che il thread di creaz connessione ritorna
            //        workerThread = new Thread(MyReceive); //creo un nuovo thread solo per gestire la connessione
            //        workerThread.Start();
            //        //MyReceive();
            //    }
            //    else
            //        MessageBox.Show("Errore di connessione ad un client");
            //});
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        //button listen method
        private void listenSocket(object sender, RoutedEventArgs e)
        {
            //il bottone può fare due cose, connettere o disconnettere, occorre distinguere 
            //i due casi
            bool isValidIp = true;
            bool isValidPorta = true;
            bool isValidPass = true;

            if (this.connesso == false && this.buttonListen.Content != "Cancel")
            {
                //this.buttonListen.IsEnabled = false;
                IPAddress address;
                Int16 port;

                //controlli sul contenuto delle boxes
                //if (!IPAddress.TryParse(this.textBoxIP.Text, out address))
                //{
                //    isValidIp = false;
                //    MessageBox.Show("IP non valido o mancante");
                //    return;
                //}

                if (!Int16.TryParse(this.textBoxPort.Text, out port) || port < 1024)
                {
                    isValidPorta = false;
                    MessageBox.Show("Porta non valida o mancante, scegliere una porta>1024");
                    return;
                }

                if (this.textBoxPassword.Password.Length == 0)
                {
                    isValidPass = false;
                    MessageBox.Show("Password mancante");
                    return;
                }

                if (isValidPass == true && isValidPorta == true && isValidIp == true)
                {

                    myEP = new IPEndPoint(IPAddress.Parse(this.textBoxIP.Text), Convert.ToInt16(this.textBoxPort.Text));
                    this.buttonListen.Content = "Cancel";
                    this.listeningTextBlock.Text = "Listening...";
                    this.textBoxIP.IsEnabled = false;
                    this.textBoxPort.IsEnabled = false;
                    this.textBoxPassword.IsEnabled = false;
                    this.pass = this.textBoxPassword.Password;
                    workerThreadConnection = new Thread(connetti);
                    workerThreadConnection.Start();
                }

            }
            else
            // else if(this.connesso == true && this.buttonListen.Content == "Cancel")
            {
                //MessageBox.Show("Ho premuto su Cancel o Disconnetti sto per annullare la richiesta!");
                disconnetti();
            }
        }

        //il metodo è richiamato sia alla pressione del bottone disconnetti sia
        //in chiusura della finestra principale dell'applicazione
        private void disconnetti()
        {
            //occorre occuparsi tramite questo metodo di eliminare tutte le connessioni attive, anche 
            //quelle ftp
            if (listener != null && connesso == false)
                listener.Close();

            if (connesso == true) // se il server e' gia' disconnesso non viene fatto nulla
            {
                _shouldStop = true; //segnalo al thread di MyReceive che deve terminare (se in esecuzione)
                if (workerThreadConnection != null)
                {
                    //workerThreadConnection.Abort();
                    workerThreadConnection.Join();
                }
                //if (workerThread != null)
                //{
                //    //workerThread.Abort();
                //    // workerThread.Join();
                //}
                if (acceptedSocket != null)
                {
                    connesso = false;

                    if (client == true) //client==true solo se e' il client a stoppare la connessione, sollevando un eccezione al server
                    {
                        //if (acceptedSocket.Connected == true)
                        //{
                        //    //acceptedSocket.Shutdown(SocketShutdown.Both);
                        //    acceptedSocket.Close();
                        //}
                        //showClientStoppedConnectionMessage();
                        acceptedSocket.Close();

                        client = false;
                    }
                    else
                    {
                        //if (acceptedSocket.Connected == true)
                        //{
                        //    //acceptedSocket.Shutdown(SocketShutdown.Both);
                        //    acceptedSocket.Close();
                        //}
                        acceptedSocket.Close();
                        this.WindowState = WindowState.Maximized;
                        ftpServer.disconnectClientConnection(); //disconnessione manuale del server ftp
                    }
                    //this.Show();
                    //acceptedSocket.Disconnect(true);
                }
                //devo disconnettere anche il ftpServer ftp
                //_shouldStop = false; //nel caso non si tratti di chiusura dell'applicazione
            }
            restoreUI();
        }

        private void connetti()
        {
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Socket handler = null;
            _shouldStop = false;
            connesso = false;
            client = false;
            try
            {
                listener.Bind(myEP);
                listener.Listen(50);
                IPEndPoint localEndPoint = (IPEndPoint)listener.LocalEndPoint;
                Console.WriteLine("Listening on {0}:{1}", localEndPoint.Address, localEndPoint.Port);

                while (!_shouldStop)
                {
                    if (listener.Poll(2000, SelectMode.SelectRead) == true) //controlla se qualcuno gli ha inviato qualcosa
                    {
                        //solo quando sono sicuro di avere richieste in coda con Poll faccio accept
                        handler = listener.Accept();
                        //handler.ReceiveTimeout = 5000; //per rendere la receive "non bloccante"
                        //inutile perchè quando il client fa connetti subito manda la password 
                        //sbloccando la receive, tranne che se la linea cade improvvisamente

                        int numBytes = handler.Receive(check);
                        string pwd = Encoding.UTF8.GetString(check);
                        //controllo password
                        if (numBytes > 0)
                        {
                            if (Encoding.UTF8.GetString(check).Trim('\0').Equals(this.pass))
                            //if (true)
                            {
                                handler.Send(check);
                                acceptedSocket = handler;
                                acceptedSocket.ReceiveTimeout = 50000;
                                break;
                            }
                            else
                            {
                                handler.Send(Encoding.UTF8.GetBytes("N"));
                                //handler.Shutdown(SocketShutdown.Both);
                                //handler.Disconnect(true); //chiude la connessione ma lascia il socket riutilizzabile
                                //handler.Close();
                                //continue;
                            }
                        }
                    }
                }
                if (!_shouldStop)
                {
                    //connessione valida stabilita
                    //chiudo il listener, senno' alla successiva chiamata di listenSocket si genera un'eccezione
                    //dovuta al fatto che ho due listener in ascolto sulla stessa porta
                    //listener.Shutdown(SocketShutdown.Both);
                    listener.Close();
                    //provo a creare anche la connessione del server ftp
                    IPEndPoint remoteEndPoint = (IPEndPoint)acceptedSocket.RemoteEndPoint;
                    ftpServer = new FtpServer(this);
                    //ftpServer.Start(remoteEndPoint.Address);
                    ftpServer.Start();
                    byte[] ftpAnswerBytes = Encoding.UTF8.GetBytes("FTPlistening");
                    acceptedSocket.BeginSend(ftpAnswerBytes, 0, ftpAnswerBytes.Length, SocketFlags.None, BeginSendCallback, acceptedSocket);
                    acceptedSocket.Receive(check);

                    if (Encoding.UTF8.GetString(check).Trim('\0').Equals("ready"))
                    {
                        connesso = true;
                        Console.WriteLine("Accepted connection from {0}:{1}.", remoteEndPoint.Address, remoteEndPoint.Port);
                    }
                    else
                    {
                        acceptedSocket.Close();
                    }
                    //dispatcher.Invoke(pc);
                    setConnectedUI();
                    return;
                }

                if (listener.Connected == true) //se ho stabilito una connessione ma poi ho chiuso il programma
                    listener.Shutdown(SocketShutdown.Both);
                listener.Close();

                Console.WriteLine("Il thread connetti sta per terminare\n");
                return;
            }
            catch (SocketException)
            {
                Console.WriteLine("Errore in fase di ascolto sul socket\n");
                if (handler != null)
                    handler.Close();
                //if (listener != null)
                //    listener.Close();
                disconnetti();
            }
            catch (System.FormatException) { Console.WriteLine("Errore!"); }
            catch (System.OverflowException) { Console.WriteLine("Errore!"); }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
        }

        /******************************/
        //metodi per il dispatcher
        private void restoreUI()
        {
            Action action = new Action(() =>
            {
                this.connesso = false;
                this.textBoxIP.IsEnabled = true;
                this.textBoxPort.IsEnabled = true;
                this.textBoxPassword.IsEnabled = true;
                this.textBoxPassword.Password = "";
                this.buttonListen.IsEnabled = true;
                this.buttonListen.Content = "Listen";
                this.listeningTextBlock.Text = "";
            });

            if (onClosing == false)
                dispatcher.Invoke(action);
        }

        private void setConnectedUI()
        {
            Action action = new Action(() =>
            {
                if (connesso == true)
                {
                    this.listeningTextBlock.Text = "";
                    Console.WriteLine("Server connesso" + "\n");
                    this.buttonListen.Content = "Disconnect";
                    this.buttonListen.IsEnabled = true; //il bottone ora permette la disconnessione dal client
                    this.WindowState = WindowState.Minimized;
                    //this.Hide();
                    //workerThreadConnection.Join(); //aspetto che il thread di creaz connessione ritorna
                    workerThread = new Thread(MyReceive); //creo un nuovo thread solo per gestire la connessione
                    workerThread.Start();
                    //MyReceive();
                }
                else
                    MessageBox.Show("Errore di connessione ad un client");
            });

            dispatcher.Invoke(action);
        }

        private void showClientStoppedConnectionMessage()
        {
            Action action = new Action(() =>
            {
                this.WindowState = WindowState.Maximized;
                //this.Show();
                MessageBox.Show("Client ha chiuso connessione!");
            });
            if (connesso == true)
                dispatcher.Invoke(action);
        }

        public void SetClip(StringCollection s)
        {
            Action action = () =>
            {
                //Console.WriteLine("Sto settando clipboard");
                Clipboard.SetFileDropList(s);
                MessageBox.Show("Clipboard copiata nel server!");
            };

            dispatcher.BeginInvoke(action);
        }

        public void SetClipText(string text)
        {
            Action action = () =>
            {
                Clipboard.SetText(text);
                MessageBox.Show("Clipboard copiata nel server!");
            };

            dispatcher.Invoke(action);
        }
        /**************************/


        /*****************************/
        //metodi connessione mouse+tastiera
        public static void BeginSendCallback(IAsyncResult ar) { }

        public void MyReceive()
        {
            while (!_shouldStop)
            {
                try
                {
                    byte[] buffer = new byte[50];
                    string bufferString;
                    byte[] stringa = Encoding.UTF8.GetBytes("D");
                    acceptedSocket.BeginSend(stringa, 0, stringa.Length, SocketFlags.None, BeginSendCallback, acceptedSocket);

                    int numBytes = acceptedSocket.Receive(buffer); //questa receive e' "non bloccante"

                    if (numBytes > 0)
                    {
                        bufferString = Encoding.UTF8.GetString(buffer);
                        parseFunction(bufferString);
                    }
                }

                catch (SocketException se)
                {
                    Console.WriteLine("errore con codice: " + se.ErrorCode);
                    //10060
                    if (se.ErrorCode != 10060) //perchè se timeout scatta viene lanciata, ma non occorre reagire!
                    { //if(se.ErrorCode==10054) //10054 quando il client fa disconnetti o cancella..io faccio la send ma mi da questa eccezione..
                        //{
                        //Action showWindow = () => {
                        //    client = true;
                        //    disconnetti();
                        //};
                        showClientStoppedConnectionMessage();

                        client = true;
                        disconnetti();
                        //dispatcher.Invoke(showWindow);
                        break;
                    }

                }
            }
            Console.WriteLine("Il thread myReceive sta per terminare\n");
        }

        private void parseFunction(string bufferString)
        {
            String car = null;
            bool isChar = false;
            int count = 0;

            foreach (char ch in bufferString)
            {
                if (ch == '\0')
                    return;

                if (ch == 'U')
                {
                    //Action mouseLeftDownOrUp = () => { Win32.mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, (uint)p.x, (uint)p.y, 0, 0); };
                    Action mouseLeftDownOrUp = () => { Win32.mouse_event(MOUSEEVENTF_LEFTUP, (uint)p.x, (uint)p.y, 0, 0); };

                    dispatcher.Invoke(mouseLeftDownOrUp);
                    continue;
                }

                if (ch == 'D')
                {
                    Action mouseLeftDown = () => { Win32.mouse_event(MOUSEEVENTF_LEFTDOWN, (uint)p.x, (uint)p.y, 0, 0); };
                    //Action mouseLeftDown = () =>
                    //{
                    //    Win32.mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, (uint)p.x, (uint)p.y, 0, 0);
                    //Thread.Sleep(150);
                    //Win32.mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, (uint)p.x, (uint)p.y, 0, 0);
                    //};
                    dispatcher.Invoke(mouseLeftDown);
                    continue;
                }

                if (ch == 'R')
                {
                    Action mouseRightDownOrUp = () => { Win32.mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, (uint)p.x, (uint)p.y, 0, 0); };
                    dispatcher.Invoke(mouseRightDownOrUp);
                    continue;
                }
                if (ch == 'W') {
                    Win32.mouse_event(0x800, 0, 0, +40,0);
                    continue;
                }
                if (ch == 'P')
                {
                    Win32.mouse_event(0x01000, 0, 0,40,0);
                    continue;
                }

                if (ch != '?' && ch != ';' && ch != '-')
                {
                    if (isChar == true)
                    {
                        car += ch;
                        count++;
                        //if (count == 3)
                        //{
                        //    count = 0;


                        //    Console.WriteLine(car);
                        //    if (car[0] == 'X')
                        //    {
                        //        InputSimulator.SimulateKeyDown((VirtualKeyCode)Convert.ToInt16(car[1].ToString()+car[2].ToString()));
                        //    }
                        //   if(car[0]=='Y')
                        //    {
                        //        InputSimulator.SimulateKeyUp((VirtualKeyCode)Convert.ToInt16(car[1].ToString()+car[2].ToString()));
                        //    }

                        //  continue;
                        //}
                        continue;
                    }
                    else
                    {
                        if (isX)
                        { coordX += Convert.ToString(ch); }
                        else
                        { coordY += Convert.ToString(ch); }
                    }
                }
                else
                {
                    if (ch == ';')
                        isX = false;

                    if (ch == '-')
                    {
                        if (isChar == true)
                        {
                            isChar = false;
                            Console.WriteLine(car);
                            if (car[0] == 'X')
                            {
                                if (count == 2)
                                {
                                    InputSimulator.SimulateKeyDown((VirtualKeyCode)Convert.ToInt16(car[1].ToString()));

                                }

                                if (count == 3)
                                {
                                    InputSimulator.SimulateKeyDown((VirtualKeyCode)Convert.ToInt16(car[1].ToString() + car[2].ToString()));

                                }
                                if (count == 4)
                                {
                                    InputSimulator.SimulateKeyDown((VirtualKeyCode)Convert.ToInt16(car[1].ToString() + car[2].ToString() + car[3].ToString()));
                                }

                                count = 0;
                                car = null;
                                continue;
                            }
                            if (car[0] == 'Y')
                            {

                                if (count == 2)
                                {
                                    InputSimulator.SimulateKeyUp((VirtualKeyCode)Convert.ToInt16(car[1].ToString()));

                                }

                                if (count == 3)
                                {
                                    InputSimulator.SimulateKeyUp((VirtualKeyCode)Convert.ToInt16(car[1].ToString() + car[2].ToString()));

                                }
                                if (count == 4)
                                {
                                    InputSimulator.SimulateKeyUp((VirtualKeyCode)Convert.ToInt16(car[1].ToString() + car[2].ToString() + car[3].ToString()));

                                }

                                count = 0;
                                car = null;
                                continue;
                            }

                        }
                        else
                        {
                            isChar = true;
                        }
                        continue;

                    }
                    else if (ch == '?')
                    {
                        isX = true;
                        if (coordX != "" && coordY != "")
                        {
                            double x_rel = Convert.ToDouble(coordX);
                            double y_rel = Convert.ToDouble(coordY);

                            p.x = Convert.ToInt16(x_rel * 1920);
                            p.y = Convert.ToInt16(y_rel * 1200);

                            coordX = "";
                            coordY = "";

                            //Console.WriteLine("x:" + p.x + " y:" + p.y + "\n");

                            Action setCurPos = () => { Win32.SetCursorPos(p.x, p.y); };
                            if (!_shouldStop)
                                dispatcher.Invoke(setCurPos);
                        }
                    }
                }
            }
        }
        /***************************/

        private void closeWindowOperations(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // MessageBox.Show("Ho chiuso finestra e ora annullo tutto!");

            //if (acceptedSocket != null)
            onClosing = true;
            disconnetti();
            //MessageBox.Show("Sto chiudendo finestra e non sono connesso!");
            //this.Hide();

        }


    }

}