using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// TCP Server 모듈
/// </summary>
class TcpServer
{
    /// <summary>
    /// Tcp Server에 접속한 Session 저장 형식
    /// </summary>
    public class TcpSession
    {
        public Socket socket;
        public string ip;

        public TcpSession(Socket socket, string ip)
        {
            this.socket = socket;
            this.ip = ip;
        }
        public void TerminateClient()
        {
            socket.Close();
        }
    }

    /// <summary>
    /// Tcp Server로 전달받은 데이터 형식
    /// </summary>
    public class ReceiveData
    {
        public string header;
        public byte[] content;
        public Socket socket;

        public ReceiveData(string header, byte[] content, Socket socket)
        {
            this.header = header;
            this.content = content;
            this.socket = socket;
        }
    }

    public delegate void ReceiveMessageHandler(ReceiveData data);
    /// <summary>
    /// TCP Server를 통해 전달받은 데이터를 처리한 이벤트
    /// </summary>
    /// <usage>
    /// tcp.OnReceiveMessage += ShowLog;
    /// 
    /// private void ShowLog(TcpServer.ReceiveData message)
    /// {
    ///     Console.WriteLine($"{message.header} : {Encoding.Default.GetString(message.content)}");
    /// }
    /// </usage>
    public event ReceiveMessageHandler OnReceiveMessage;

    public List<TcpSession> sessionList;

    private Socket tcpSocket = null;
    private Thread connectThread = null;
    private Thread receiveThread = null;

    private Queue<ReceiveData> receiveDataQueue;

    private int maxClientCount = 0;
    private readonly int headerSize = 10;
    private readonly int maxPacketSize = 1024;

    public TcpServer()
    {
        sessionList = new List<TcpSession>();
        receiveDataQueue = new Queue<ReceiveData>();

        // TODO: Delete After Test
        OnReceiveMessage += new ReceiveMessageHandler(OnReceiveTcpMessage);
    }

    /// <summary>
    /// TCP 모듈을 초기화시켜준다.
    /// </summary>
    /// <param name="port">열어놓을 포트</param>
    /// <param name="maxClientCount">접속을 허용할 최대 Client의 수</param>
    /// <usage>
    /// TcpServer tcp = new TcpServer();
    /// tcp.InitializeServer(tcpPort, maxClientCount);
    /// </usage>
    public void InitializeServer(int port, int maxClientCount)
    {
        this.maxClientCount = maxClientCount;

        IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, port);
        tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        tcpSocket.Bind(ipEndPoint);
        tcpSocket.Listen(10);

        receiveThread = new Thread(InvokeMessageEvent);
        receiveThread.Start();

        connectThread = new Thread(new ThreadStart(WaitClient));
        connectThread.Start();
    }
    /// <summary>
    /// TCP 서버를 통해 데이터를 전달한다.
    /// </summary>
    /// <param name="header">전달할 데이터의 헤더, 크기는 10 Byte까지다.</param>
    /// <param name="contentsData">전달할 데이터</param>
    /// <param name="clientSocket">전달할 대상의 소켓</param>
    /// <usage>
    /// tcp.SendMessage("Command", datas, tcp.sessionList[0].socket);
    /// </usage>
    public void SendMessage(string header, byte[] contentsData, Socket clientSocket)
    {
        if (!clientSocket.Connected)
        {
            return;
        }

        byte[] headerData = Encoding.UTF8.GetBytes(header);
        Array.Resize(ref headerData, headerSize);

        byte[] sendData = new byte[headerData.Length + contentsData.Length];
        Array.Copy(headerData, sendData, headerData.Length);
        Array.Copy(contentsData, 0, sendData, headerData.Length, contentsData.Length);

        int dataLength = sendData.Length;

        byte[] dataSize = new byte[4];
        dataSize = BitConverter.GetBytes(dataLength);
        clientSocket.Send(dataSize);

        int cumulativeDataLength = 0;
        int totalDataLength = dataLength;
        int sendDataLength = 0;

        while (cumulativeDataLength < dataLength)
        {
            sendDataLength = clientSocket.Send(sendData, cumulativeDataLength, totalDataLength, SocketFlags.None);
            cumulativeDataLength += sendDataLength;
            totalDataLength -= sendDataLength;
        }
    }
    public void Terminate()
    {
        connectThread.Interrupt();
        connectThread.Join();
        receiveThread.Interrupt();
        receiveThread.Join();

        tcpSocket.Close();
    }
    /// <summary>
    /// Client로부터 Disconnect 신호를 받고, 해당 클라이언트를 삭제한다.
    /// </summary>
    /// <param name="targetSocket">신호를 보낸 클라이언트의 소켓</param>
    public void RemoveTerminatedClient(Socket targetSocket)
    {
        TcpSession targetSession = null;

        foreach (TcpSession tcpSession in sessionList)
        {
            if (tcpSession.socket == targetSocket)
            {
                targetSession = tcpSession;
            }
        }

        targetSession.TerminateClient();

        sessionList.Remove(targetSession);
    }

    private void WaitClient()
    {
        try
        {
            while (true)
            {
                if (sessionList.Count < maxClientCount)
                {
                    Socket client = tcpSocket.Accept();

                    IPEndPoint ip = (IPEndPoint)client.RemoteEndPoint;

                    RemoveTerminatedClients();

                    TcpSession tcpSession = new TcpSession(client, ip.Address.ToString());
                    sessionList.Add(tcpSession);

                    SendMessage("string", Encoding.Default.GetBytes("HelloWorld!"), client);

                    Thread thread = new Thread(new ParameterizedThreadStart(ListenMessage));
                    thread.Start(client);
                }
            }
        }
        catch (ThreadInterruptedException e)
        {

        }


    }
    private void ListenMessage(object socket)
    {
        try
        {
            Socket clientSocket = (Socket)socket;

            while (true)
            {
                ReceiveData receivedTcpData = ReceiveMessage(clientSocket);

                if (receivedTcpData == null)
                    break;

                receiveDataQueue.Clear();
                receiveDataQueue.Enqueue(receivedTcpData);
            }
        }
        catch (ThreadInterruptedException e)
        {

        }


    }
    private ReceiveData ReceiveMessage(Socket clientSocket)
    {
        try
        {
            if (!clientSocket.Connected)
                return null;

            Console.WriteLine();

            int dataLength;

            byte[] dataSize = new byte[4];
            clientSocket.Receive(dataSize, 0, 4, SocketFlags.None);
            dataLength = BitConverter.ToInt32(dataSize, 0);

            Console.WriteLine($"DataLength: {dataLength}");

            if (dataLength == 0)
                return null;

            byte[] receivedData = new byte[dataLength];
            int remainDataLength = dataLength;
            int cumulativeDataLength = 0;
            int receivedDataLength = 0;

            while (cumulativeDataLength < dataLength)
            {
                if (remainDataLength > maxPacketSize)
                {
                    receivedDataLength = clientSocket.Receive(receivedData, cumulativeDataLength, maxPacketSize, 0);
                }
                else
                {
                    receivedDataLength = clientSocket.Receive(receivedData, cumulativeDataLength, remainDataLength, 0);
                }

                Console.WriteLine($"받은 양? {receivedDataLength}");

                if (receivedDataLength == 0)
                    break;

                cumulativeDataLength += receivedDataLength;
                remainDataLength -= receivedDataLength;

                Console.WriteLine($"누적량 {cumulativeDataLength}");
                Console.WriteLine($"받아야할 용량 {remainDataLength}");
                Console.WriteLine("--------------------------------");
            }

            byte[] headerData = new byte[headerSize];
            byte[] contentsData = new byte[dataLength - headerSize];

            Array.Copy(receivedData, 0, headerData, 0, headerSize);
            Array.Copy(receivedData, headerSize, contentsData, 0, dataLength - headerSize);

            Console.WriteLine($"{headerData.Length}");
            Console.WriteLine($"{contentsData.Length}");

            ReceiveData receivedTcpData = new ReceiveData(Encoding.Default.GetString(headerData), contentsData, clientSocket);

            return receivedTcpData;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);

            return null;
        }
    }
    private void InvokeMessageEvent()
    {
        while (true)
        {
            if (receiveDataQueue.Count > 0)
            {
                OnReceiveMessage.Invoke(receiveDataQueue.Dequeue());
            }

            Thread.Sleep(30);
        }
    }
    private void RemoveTerminatedClients()
    {
        List<TcpSession> terminatedSessionList = new List<TcpSession>();

        foreach (TcpSession tcpSession in sessionList)
        {
            if (!IsClientConnected(tcpSession.socket))
            {

                tcpSession.TerminateClient();

                terminatedSessionList.Add(tcpSession);
            }
        }

        foreach (TcpSession targetPlayer in terminatedSessionList)
        {
            sessionList.Remove(targetPlayer);
        }
    }
    private bool IsClientConnected(Socket socket)
    {
        try
        {
            return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
        }
        catch (SocketException)
        {
            return false;
        }
    }


    // TODO: Delete After Test
    private void OnReceiveTcpMessage(ReceiveData receivedData)
    {
        Console.WriteLine(Encoding.Default.GetString(receivedData.content));

        SendMessage("string", Encoding.UTF8.GetBytes("TESTing....."), receivedData.socket);
    }
}