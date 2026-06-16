using System.Net;
using System.Net.Sockets;
using System.IO;
using UnityEngine;
using System;
using System.Threading;

public class KinectNetworkSender : MonoBehaviour
{
    public int port;
    private TcpListener _listener;
    private TcpClient _client;
    private NetworkStream _stream;
    private bool _isRunning = true;

    // ref
    private KinectController _kinectController;

    void Start()
    {
        _kinectController = FindObjectOfType<KinectController>();
        if (_kinectController == null)
        {
            print("requires a kinect controller");
            return;
        }

        Thread serverThread = new Thread(StartServer);
        serverThread.Start();
    }

    void StartServer()
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        print("Waiting Receiver");

        _client = _listener.AcceptTcpClient();
        _stream = _client.GetStream();
        print("Receiver Connected");

        // wait for kinect init
        while (!_kinectController.kinectInitialized && _isRunning) {
            Thread.Sleep(100);
        }
        // sending calib params, the order must be the same as how Receiver reads them
        SendCalibrationData();

        // sending image data
        while (_isRunning)
        {
            if (_kinectController.m_colorImage != null)
            {
                lock (_kinectController.m_bufferLock)
                {
                    _stream.Write(_kinectController.m_colorImage, 0, _kinectController.m_colorImage.Length);
                    _stream.Write(_kinectController.m_depthImage, 0, _kinectController.m_depthImage.Length);
                    _stream.Write(_kinectController.m_bodyIndexMap, 0, _kinectController.m_bodyIndexMap.Length);
                }
            }
            Thread.Sleep(33); // approximately 30 FPS
        }
    }

    void SendCalibrationData()
    {
        // send Width, Height, CalibSize
        byte[] w = BitConverter.GetBytes(_kinectController.depthWidth);
        byte[] h = BitConverter.GetBytes(_kinectController.depthHeight);
        byte[] size = BitConverter.GetBytes(_kinectController.calibrationTable.Length);
        
        _stream.Write(h, 0, 4);
        _stream.Write(w, 0, 4);
        _stream.Write(size, 0, 4);

        // send Calibration Table
        foreach (float val in _kinectController.calibrationTable)
        {
            _stream.Write(BitConverter.GetBytes(val), 0, 4);
        }
    }

    void OnApplicationQuit()
    {
        _isRunning = false;
        if (_client != null) _client.Close();
        if (_listener != null) _listener.Stop();
    }
}
