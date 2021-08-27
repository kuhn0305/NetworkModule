using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// TCP Client 모듈
/// </summary>
public class TcpClient
{
    /// <summary>
    /// Tcp Client로 전달받은 데이터 형식
    /// </summary>
    public class ReceiveData
    {
        public string header;
        public byte[] content;

        public ReceiveData(string header, byte[] content)
        {
            this.header = header;
            this.content = content;
        }
    }

    private class SendData
    {
        public string header;
        public byte[] content;

        public SendData(string header, byte[] content)
        {
            this.header = header;
            this.content = content;
        }
    }

    public delegate void ReceiveMessageHandler(ReceiveData data);
    /// <summary>
    /// TCP Client를 통해 전달받은 데이터를 처리한 이벤트
    /// </summary>
    /// <usage>
    /// tcp.OnReceiveMessage += ShowLog;
    /// 
    /// private void ShowLog(TcpClient.ReceiveData message)
    /// {
    ///     Console.WriteLine($"{message.header} : {Encoding.Default.GetString(message.content)}");
    /// }
    /// </usage>
    public event ReceiveMessageHandler OnReceiveMessage;

    public delegate void LogDelegate(string message);
    public event LogDelegate Log;

    public Socket tcpSocket = null;
    private Thread connectThread = null;
    private Thread receiveThread = null;
    private Thread invokeMessageThread = null;
    private Thread waitMessageTrhead = null;

    private Queue<ReceiveData> receiveDataQueue;

    private int reconnectCount;
    private string serverIp;
    private int port;
    private int maxReconnectCount;

    private readonly int headerSize = 10;
    private readonly int maxPacketSize = 1024;

    private Queue<SendData> sendDataQueue;

    private bool isSendProcessWorking = false;

    public TcpClient(int maxReconnectCount)
    {
        this.maxReconnectCount = maxReconnectCount;
        reconnectCount = maxReconnectCount;

        receiveDataQueue = new Queue<ReceiveData>();
        sendDataQueue = new Queue<SendData>();

        invokeMessageThread = new Thread(InvokeMessageEvent);
        invokeMessageThread.Start();

        waitMessageTrhead = new Thread(InvokeSendMessageEvent);
        waitMessageTrhead.Start();
    }
    /// <summary>
    /// TCP 모듈을 초기화시켜준다.
    /// </summary>
    /// <param name="serverIp">접속할 Server의 IP</param>
    /// <param name="port">열어놓을 포트</param>
    /// <usage>
    /// TcpClient tcp = new TcpServer();
    /// tcp.InitializeServer(tcpPort);
    /// </usage>
    public void InitializeClient(string serverIp, int port)
    {
        this.serverIp = serverIp;
        this.port = port;

        IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), port);
        tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        connectThread = new Thread(new ParameterizedThreadStart(Connect));
        connectThread.Start(serverEndPoint);
    }
    /// <summary>
    /// TCP 클라이언트를 통해 데이터를 전달한다.
    /// </summary>
    /// <param name="header">전달할 데이터의 헤더, 크기는 10 Byte까지다.</param>
    /// <param name="contentsData">전달할 데이터</param>
    /// <usage>
    /// tcp.SendMessage("Command", datas);
    /// </usage>
    public void SendMessage(string header, byte[] contentsData)
    {
        try
        {
            if (isSendProcessWorking)
            {
                SendData sendDataInfo = new SendData(header, contentsData);

                sendDataQueue.Enqueue(sendDataInfo);

                return;
            }

            isSendProcessWorking = true;

            if (!tcpSocket.Connected)
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
            tcpSocket.Send(dataSize);

            int cumulativeDataLength = 0;
            int remainDataLength = dataLength;
            int sendDataLength = 0;

            while(cumulativeDataLength < dataLength)
            {
                if(remainDataLength > maxPacketSize)
                {
                    sendDataLength = tcpSocket.Send(sendData, cumulativeDataLength, maxPacketSize, SocketFlags.None);

                }
                else
                {
                    sendDataLength = tcpSocket.Send(sendData, cumulativeDataLength, remainDataLength, SocketFlags.None);

                }
                cumulativeDataLength += sendDataLength;
                remainDataLength -= sendDataLength;
            }

            isSendProcessWorking = false;
        }
        catch(Exception e)
        {
            Log?.Invoke(e.Message);
        }

    }
    public void Terminate()
    {
        connectThread?.Abort();
        receiveThread?.Abort();
        invokeMessageThread?.Abort();
        tcpSocket.Close();
    }

    private void Connect(object endPoint)
    {
        try
        {
            IPEndPoint ipEndpoint = (IPEndPoint)endPoint;

            IAsyncResult connectResult = tcpSocket.BeginConnect(ipEndpoint, null, null);
            bool success = connectResult.AsyncWaitHandle.WaitOne(5000, true);

            if(tcpSocket.Connected)
            {
                tcpSocket.EndConnect(connectResult);
                receiveDataQueue.Clear();
                receiveThread = new Thread(ReceiveMessage);
                receiveThread.Start();

                reconnectCount = maxReconnectCount;
            }
            else
            {
                tcpSocket.Close();
                throw new SocketException((int)SocketError.TimedOut);
            }
        }
        catch(SocketException e)
        {
            Log?.Invoke(e.Message);
            if(e.ErrorCode == (int)SocketError.TimedOut && reconnectCount-- != 0)
            {
                Thread.Sleep(3000);
                InitializeClient(serverIp, port);
            }
        }
        catch(Exception e)
        {
            Log?.Invoke(e.Message);
        }
    }
    private void ReceiveMessage()
    {
        try
        {
            while(true)
            {
                byte[] dataSize = new byte[4];
                tcpSocket.Receive(dataSize, 0, 4, SocketFlags.None);
                int dataLength = BitConverter.ToInt32(dataSize, 0);

                if (dataLength == 0)
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }

                byte[] receivedData = new byte[dataLength];
                int remainDataLength = dataLength;
                int cumulativeDataLength = 0;
                int receivedDataLength = 0;

                while(cumulativeDataLength < dataLength)
                {
                    if(remainDataLength > maxPacketSize)
                    {
                        receivedDataLength = tcpSocket.Receive(receivedData, cumulativeDataLength, maxPacketSize, 0);
                    }
                    else
                    {
                        receivedDataLength = tcpSocket.Receive(receivedData, cumulativeDataLength, remainDataLength, 0);
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

                ReceiveData receivedTcpData = new ReceiveData(Encoding.Default.GetString(headerData), contentsData);

                receiveDataQueue.Enqueue(receivedTcpData);
            }
        }
        catch(SocketException e)
        {
            Log?.Invoke(e.Message);
            if(e.ErrorCode == (int)SocketError.ConnectionReset)
            {
                InitializeClient(serverIp, port);
            }
        }
        catch(Exception e)
        {
            Log?.Invoke(e.Message);
        }
    }
    private void InvokeMessageEvent()
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
                SendMessage(sendData.header, sendData.content);
            }
        }
    }
}