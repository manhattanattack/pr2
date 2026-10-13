using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using NativeWebSocket;

[Serializable]
public class PlayerStateData
{
    public string id;
    public string name;
    public float x, y, z;
    public float yaw, pitch;
    public bool crouch;
}

[Serializable]
public class SnapshotPacket
{
    public string type;
    public PlayerStateData[] players;
}

[Serializable]
public class PlayerLeftPacket
{
    public string type;
    public string id;
}

public class NetworkClient : MonoBehaviour
{
    [Header("Префабы")]
    [SerializeField] private GameObject localPlayerPrefab;
    [SerializeField] private GameObject networkPlayerPrefab;

    [Header("Лабиринт")]
    [SerializeField] private MazeBuilder mazeBuilder;

    [Header("Миникарта")]
    [SerializeField] private MinimapBaker minimapBaker;
    [SerializeField] private MinimapMarkersManager markersManager;
    [SerializeField] private Transform mazeRoot;

    private WebSocket websocket;
    private string myClientId;
    private GameObject localPlayerInstance;
    private LocalPlayerController localController;

    private readonly Dictionary<string, NetworkPlayer> remotePlayers = new Dictionary<string, NetworkPlayer>();

    private float sendTimer = 0f;
    private const float SendInterval = 0.05f; // 20 раз в секунду

    // Доступ для миникарты
    public Transform LocalPlayerTransform => localPlayerInstance != null ? localPlayerInstance.transform : null;
    public IReadOnlyDictionary<string, NetworkPlayer> RemotePlayers => remotePlayers;

    private async void Start()
    {
        string serverUrl = $"ws://{LobbyManager.ServerIP}:{LobbyManager.ServerPort}";
        Debug.Log($"[NetworkClient] Подключение к {serverUrl}...");

        websocket = new WebSocket(serverUrl);

        websocket.OnOpen += async () =>
        {
            Debug.Log("[NetworkClient] Соединение открыто! Передача имени из лобби...");
            string joinJson = $"{{\"type\":\"JOIN\",\"name\":\"{LobbyManager.Nickname}\"}}";
            await websocket.SendText(joinJson);
        };

        websocket.OnMessage += (bytes) =>
        {
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            HandleServerMessage(json);
        };

        websocket.OnError += (err) => Debug.LogError($"[NetworkClient] Ошибка: {err}");
        websocket.OnClose += (code) => Debug.LogWarning($"[NetworkClient] Соединение закрыто. Код: {code}");

        await websocket.Connect();
    }

    private void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        if (websocket != null)
        {
            websocket.DispatchMessageQueue();
        }
#endif

        if (localPlayerInstance != null && websocket != null && websocket.State == WebSocketState.Open)
        {
            sendTimer += Time.deltaTime;
            if (sendTimer >= SendInterval)
            {
                sendTimer = 0f;
                SendMovementUpdate();
            }
        }
    }

    private void HandleServerMessage(string json)
    {
        if (json.Contains("\"INIT_WORLD\""))
        {
            InitWorldPacket packet = JsonUtility.FromJson<InitWorldPacket>(json);
            myClientId = packet.id;

            // 1. Постройка лабиринта
            if (mazeBuilder != null)
            {
                mazeBuilder.BuildFromNetworkData(packet.walls);
            }

            // 2. Однократное запекание схемы лабиринта в статическую текстуру
            if (minimapBaker != null && mazeRoot != null)
            {
                minimapBaker.Bake(mazeRoot);
            }

            // 3. Спавн локального игрока и регистрация на миникарте
            Vector3 spawnPos = new Vector3(packet.spawnPos[0], packet.spawnPos[1], packet.spawnPos[2]);
            SpawnLocalPlayer(spawnPos);

            if (markersManager != null)
            {
                markersManager.RegisterLocalPlayer(localPlayerInstance.transform);
            }
        }
    }

    private void SpawnLocalPlayer(Vector3 spawnPos)
    {
        if (localPlayerInstance != null) return;

        localPlayerInstance = Instantiate(localPlayerPrefab, spawnPos, Quaternion.identity);
        localController = localPlayerInstance.GetComponent<LocalPlayerController>();
    }

    private void ProcessWorldSnapshot(SnapshotPacket snapshot)
    {
        if (string.IsNullOrEmpty(myClientId)) return;

        foreach (var data in snapshot.players)
        {
            // Собственный аватар не дублируем
            if (data.id == myClientId) continue;

            Vector3 netPos = new Vector3(data.x, data.y, data.z);

            if (!remotePlayers.ContainsKey(data.id))
            {
                GameObject remoteObj = Instantiate(networkPlayerPrefab, netPos, Quaternion.Euler(0f, data.yaw, 0f));
                NetworkPlayer np = remoteObj.GetComponent<NetworkPlayer>();
                np.Initialize(data.name, netPos);

                remotePlayers.Add(data.id, np);

                // Регистрация маркера нового сокурсника на миникарте
                if (markersManager != null)
                {
                    markersManager.RegisterRemotePlayer(data.id, remoteObj.transform);
                }
            }
            else
            {
                remotePlayers[data.id].UpdateNetworkState(netPos, data.yaw, data.pitch, data.crouch);
            }
        }
    }

    private void RemoveRemotePlayer(string id)
    {
        if (remotePlayers.TryGetValue(id, out NetworkPlayer player))
        {
            Destroy(player.gameObject);
            remotePlayers.Remove(id);

            // Удаление маркера с миникарты
            if (markersManager != null)
            {
                markersManager.UnregisterRemotePlayer(id);
            }
        }
    }

    private async void SendMovementUpdate()
    {
        Transform tr = localPlayerInstance.transform;
        CultureInfo ci = CultureInfo.InvariantCulture;

        string moveJson = "{\"type\":\"MOVE\"," +
            "\"x\":" + tr.position.x.ToString("F2", ci) + "," +
            "\"y\":" + tr.position.y.ToString("F2", ci) + "," +
            "\"z\":" + tr.position.z.ToString("F2", ci) + "," +
            "\"yaw\":" + tr.eulerAngles.y.ToString("F1", ci) + "," +
            "\"pitch\":" + localController.PitchAngle.ToString("F1", ci) + "," +
            "\"crouch\":" + (localController.IsCrouching ? "true" : "false") + "}";

        await websocket.SendText(moveJson);
    }

    private async void OnApplicationQuit()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.Close();
        }
    }
}