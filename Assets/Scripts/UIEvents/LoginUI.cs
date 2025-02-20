using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;

public class LoginUI : MonoBehaviour, IConnectionCallbacks
{
    private bool _isTryingToConnect = false;
    private float _connectionTimeout = 10.0f; // 10 seconds for timeout
    private float _connectionStartTime;
    private AudioClip _buttonClickSound;
    private AudioSource _audioSource;
    private bool _isButtonClicked;

    // Start is called before the first frame update
    void Start()
    {
        _audioSource = transform.Find("audioSource").GetComponent<AudioSource>();
        _buttonClickSound = Resources.Load<AudioClip>("Button");
        _isButtonClicked = false;

        var startBtn = transform.Find("startBtn").GetComponent<Button>();
        startBtn.onClick.AddListener(OnStartBtn);
        startBtn.onClick.AddListener(PlayButtonClickSound); 

        var quitBtn = transform.Find("quitBtn").GetComponent<Button>();
        quitBtn.onClick.AddListener(OnQuitBtn);
        quitBtn.onClick.AddListener(PlayButtonClickSound);

        PhotonNetwork.AddCallbackTarget(this);
    }
    
    private void PlayButtonClickSound()
    {
        if (_audioSource != null && _buttonClickSound != null)
        {
            _audioSource.PlayOneShot(_buttonClickSound);
        }
    }


    public void OnStartBtn()
    {
        if (!_isButtonClicked)
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                // Debug.Log("No internet connection available. Please connect to the internet and try again.");
                return; // Exit if no internet
            }

            Game.uiManager.ShowUI<MaskUI>("MaskUI").ShowMask("Loading...");

            _isTryingToConnect = true;
            _connectionStartTime = Time.time;
            PhotonNetwork.ConnectUsingSettings();
        }
        else
        {

        }
    }

    private void Update()
    {
        if (_isTryingToConnect && (Time.time - _connectionStartTime > _connectionTimeout))
        {
            // Timeout logic
            _isTryingToConnect = false;
            PhotonNetwork.Disconnect();
            Game.uiManager.CloseUI("MaskUI");
            // Debug.Log("Connection timed out. Please check your network and try again.");
        }
    }

    public void OnQuitBtn()
    {
        Application.Quit();
    }

    private void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    private void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    public void OnConnected()
    {

    }

    public void OnConnectedToMaster()
    {
        _isTryingToConnect = false;
        Game.uiManager.CloseAllUI();
        // Debug.Log("Connected to master");
        Game.uiManager.ShowUI<LobbyUI>("LobbyUI");
        PhotonNetwork.NetworkStatisticsEnabled = true;
    }

    public void OnDisconnected(DisconnectCause cause)
    {
        _isTryingToConnect = false;
        Game.uiManager.CloseUI("MaskUI");
        Debug.Log($"Disconnected: {cause}");
    }

    public void OnRegionListReceived(RegionHandler regionHandler)
    {

    }

    public void OnCustomAuthenticationResponse(Dictionary<string, object> data)
    {

    }

    public void OnCustomAuthenticationFailed(string debugMessage)
    {

    }
}
