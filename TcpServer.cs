using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// TCP Server 모듈
/// </summary>
public class TcpServer
{
    /// <summary>
    /// Tcp Server에 접속한 Session 저장 형식
    /// </summary>
    public class TcpSession
    {
        private int index = -1;

        public int Index
        {
            get { return index; }
            set { index = value; }
        }

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

    private class SendData
    {
        public string header;
        public byte[] content;
        public TcpSession session;

        public SendData(string header, byte[] content, TcpSession session)
        {
            this.header = header;
            this.content = content;
            this.session = session;
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

    public delegate void SessionChangedEventHandler(TcpSession clientSession);
    public event SessionChangedEventHandler OnConnectAccept;
    public event SessionChangedEventHandler OnTerminate;

    public delegate void TransferProcessingHandler(float percent);
    public event TransferProcessingHandler OnProcessing;

    public delegate void LogEventHandler(string message);
    public event LogEventHandler Log;

    public List<TcpSession> sessionList;

    private Socket tcpSocket = null;
    private Thread waitThread = null;
    private Thread invokeMessageThread = null;
    private Thread waitMessageTrhead = null;
    private Thread findDisconnectedThread = null; 

    private Queue<ReceiveData> receiveDataQueue;

    private int maxClientCount = 0;
    private readonly int headerSize = 10;
    private readonly int maxPacketSize = 1024;
    private bool isMaxConnection;
    private IPEndPoint ipEndPoint;

    private Queue<SendData> sendDataQueue;

    private bool isSendProcessWorking = false;

    public TcpServer()
    {
        sessionList = new List<TcpSession>();
        receiveDataQueue = new Queue<ReceiveData>();
        sendDataQueue = new Queue<SendData>();

        invokeMessageThread = new Thread(InvokeReceiveMessageEvent);
        invokeMessageThread.Start();

        waitMessageTrhead = new Thread(InvokeSendMessageEvent);
        waitMessageTrhead.Start();

        findDisconnectedThread = new Thread(RemoveTerminatedClients);
        findDisconnectedThread.Start();
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

        ipEndPoint = new IPEndPoint(IPAddress.Any, port);

        tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        tcpSocket.Bind(ipEndPoint);
        tcpSocket.Listen(10);

        waitThread = new Thread(WaitClient);
        waitThread.Start();
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
    /// 
    public void SendMessage(string header, byte[] contentsData, TcpSession clientSession)
    {
        try
        {
            if (!clientSession.socket.Connected)
            {
                return;
            }

            if (isSendProcessWorking)
            {
                SendData sendDataInfo = new SendData(header, contentsData, clientSession);

                sendDataQueue.Enqueue(sendDataInfo);

                return;
            }

            isSendProcessWorking = true;

            byte[] headerData = Encoding.Default.GetBytes(header);
            Array.Resize(ref headerData, headerSize);

            byte[] sendData = new byte[headerData.Length + contentsData.Length];
            Array.Copy(headerData, sendData, headerData.Length);
            Array.Copy(contentsData, 0, sendData, headerData.Length, contentsData.Length);

            int dataLength = sendData.Length;

            byte[] dataSize = new byte[4];
            dataSize = BitConverter.GetBytes(dataLength);
            clientSession.socket.Send(dataSize);

            int cumulativeDataLength = 0;
            int remainDataLength = (int)dataLength;
            int sendDataLength = 0;

            while (cumulativeDataLength < dataLength)
            {
                OnProcessing?.Invoke(((float)cumulativeDataLength / (float)dataLength) * 100);

                if (remainDataLength > maxPacketSize)
                {
                    sendDataLength = clientSession.socket.Send(sendData, cumulativeDataLength, maxPacketSize, SocketFlags.None);
                }
                else
                {
                    sendDataLength = clientSession.socket.Send(sendData, cumulativeDataLength, remainDataLength, SocketFlags.None);
                }

                cumulativeDataLength += sendDataLength;
                remainDataLength -= sendDataLength;
            }

            OnProcessing?.Invoke((float)100);

            isSendProcessWorking = false;
        }
        catch (Exception e)
        {
            isSendProcessWorking = false;

            Console.WriteLine(e.StackTrace);
        }
    }
    public void Terminate()
    {
        waitThread?.Abort();
        invokeMessageThread?.Abort();
        tcpSocket.Close();
    }
    /// <summary>
    /// Client로부터 Disconnect 신호를 받고, 해당 클라이언트를 삭제한다.
    /// </summary>
    /// <param name="targetSocket">신호를 보낸 클라이언트의 소켓</param>
    public void TerminateClient(Socket targetSocket)
    {
        TcpSession targetSession = sessionList.Find(s => s.socket == targetSocket);

        if(targetSession != null)
        {
            OnTerminate?.Invoke(targetSession);

            targetSession.TerminateClient();
            sessionList.Remove(targetSession);

        }
    }

    private void WaitClient()
    {
        try
        {
            while(true)
            {
                // 탐색 이후 Session List의 갯수를 확인한다.
                if(sessionList.Count < maxClientCount)
                {
                    if(isMaxConnection)
                    {
                        tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        tcpSocket.Bind(ipEndPoint);
                        tcpSocket.Listen(10);
                        isMaxConnection = false;
                    }
                    Socket client = tcpSocket.Accept();
                    IPEndPoint ip = (IPEndPoint)client.RemoteEndPoint;

                    TcpSession tcpSession = new TcpSession(client, ip.Address.ToString());
                    sessionList.Add(tcpSession);
                    OnConnectAccept?.Invoke(tcpSession);

                    Thread listenThread = new Thread(new ParameterizedThreadStart(ListenMessage));
                    listenThread.Start(client);
                }
                else
                {
                    tcpSocket.Close();
                    isMaxConnection = true;
                }
            }
        }
        catch(Exception e)
        {
            Log?.Invoke(e.Message);
        }
    }
    private void ListenMessage(object socket)
    {
        try
        {
            Socket clientSocket = (Socket)socket;

            while(true)
            {
                ReceiveData receivedTcpData = ReceiveMessage(clientSocket);
                if(receivedTcpData == null)
                {
                    break;
                }

                receiveDataQueue.Enqueue(receivedTcpData);
            }
        }
        catch(Exception e)
        {
            Log?.Invoke(e.Message);
        }
    }
    private ReceiveData ReceiveMessage(Socket clientSocket)
    {
        try
        {
            if(!clientSocket.Connected)
            {
                throw new SocketException((int)SocketError.NotConnected);
            }

            byte[] dataSize = new byte[4];
            clientSocket.Receive(dataSize, 0, 4, SocketFlags.None);
            int dataLength = BitConverter.ToInt32(dataSize, 0);

            if(dataLength == 0)
            {
                throw new SocketException((int)SocketError.NetworkUnreachable);
            }

            // 헤더 받은 후, 널 값 체크
            byte[] receivedData = new byte[dataLength];
            int remainDataLength = dataLength;
            int cumulativeDataLength = 0;
            int receivedDataLength = 0;

            while(cumulativeDataLength < dataLength)
            {
                if(remainDataLength > maxPacketSize)
                {
                    receivedDataLength = clientSocket.Receive(receivedData, cumulativeDataLength, maxPacketSize, 0);
                }
                else
                {
                    receivedDataLength = clientSocket.Receive(receivedData, cumulativeDataLength, remainDataLength, 0);
                }

                if(receivedDataLength == 0)
                {
                    break;
                }

                cumulativeDataLength += receivedDataLength;
                remainDataLength -= receivedDataLength;
            }

            byte[] headerData = new byte[headerSize];
            byte[] contentsData = new byte[dataLength - headerSize];

            Array.Copy(receivedData, 0, headerData, 0, headerSize);
            headerData = Array.FindAll(headerData, o => o != 0);
            Array.Copy(receivedData, headerSize, contentsData, 0, dataLength - headerSize);

            ReceiveData receivedTcpData = new ReceiveData(Encoding.Default.GetString(headerData), contentsData, clientSocket);

            return receivedTcpData;
        }
        catch(SocketException e)
        {
            Log?.Invoke(e.Message);
            TerminateClient(clientSocket);
        }
        catch(Exception e)
        {
            Log?.Invoke(e.Message);
        }

        return null;
    }
    private void InvokeReceiveMessageEvent()
    {
        while(true)
        {
            if(receiveDataQueue.Count > 0)
            {
                ReceiveData receiveData = receiveDataQueue.Dequeue();
                OnReceiveMessage?.Invoke(receiveData);
            }
        }
    }
    private void InvokeSendMessageEvent()
    {
        while (true)
        {
            if (!isSendProcessWorking && sendDataQueue.Count > 0)
            {
                SendData sendData = sendDataQueue.Dequeue();
                SendMessage(sendData.header, sendData.content, sendData.session);
            }
        }
    }
    private void RemoveTerminatedClients()
    {
        while(true)
        {
            List<TcpSession> terminatedSessionList = new List<TcpSession>();

            foreach (TcpSession tcpSession in sessionList)
            {
                if (!IsClientConnected(tcpSession.socket))
                {
                    terminatedSessionList.Add(tcpSession);
                }
            }

            foreach (TcpSession targetSession in terminatedSessionList)
            {
                TerminateClient(targetSession.socket);
            }

            Thread.Sleep(1000);
        }
    }
    private bool IsClientConnected(Socket socket)
    {
        try
        {
            return socket.Connected;
        }
        catch(SocketException e)
        {
            Log?.Invoke(e.Message);
            return false;
        }
    }
}