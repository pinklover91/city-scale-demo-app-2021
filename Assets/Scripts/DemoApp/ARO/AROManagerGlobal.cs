using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Parse.LiveQuery;
using Parse;
using Immersal.AR;

namespace Immersal.Samples.DemoApp.ARO
{
    public class AROManagerGlobal : MonoBehaviour
    {
		private static AROManagerGlobal instance = null;

        [SerializeField]
        private List<GameObject> prefabs;
        [SerializeField]
        private GameObject defaultPrefab;
        [SerializeField]
        private Transform goContainer;
        [SerializeField]
        private GameObject m_UICanvas;
        [SerializeField]
        private AROPlacer AROPlacer;
        private ParseClient m_ParseClient;
        private ParseLiveQueryClient m_ParseLiveClient;
        private RealtimeQuery<ParseObject> m_RealtimeQuery;
        private Dictionary<string, GameObject> m_GOs;
        private Dictionary<string, ParseObject> m_AROs;

        private bool isInitialized = false;

        public ParseObject currentScene { get; set; }
        public Transform GOContainer { get => goContainer; set => goContainer = value; }
        public bool IsInitialized { get => isInitialized; }
        /// <summary>
        /// This is just a little bool that tracks whether we are subcribed, it is a bit of a hack to show the emuilation of entry for new subcribers to the realtimequyery
        /// </summary>
        public bool m_IsSubscribed;

        public int currentMapId
        {
            get
            {
                return currentScene != null ? currentScene.Get<int>("mapId") : -1;
            }
        }

        public static AROManagerGlobal Instance
        {
            get
            {
#if UNITY_EDITOR
                if (instance == null && !Application.isPlaying)
                {
                    instance = UnityEngine.Object.FindObjectOfType<AROManagerGlobal>();
                }
#endif
                if (instance == null)
                {
                    Debug.LogError("No AROManagerGlobal instance found. Ensure one exists in the scene.");
                }
                return instance;
            }
        }

        void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            if (instance != this)
            {
                Debug.LogError("There must be only one AROManagerGlobal object in a scene.");
                UnityEngine.Object.DestroyImmediate(this);
                return;
            }
        }

        public void Reset()
        {
            isInitialized = false;
            DeleteAllGOs();
            m_GOs = null;
            m_AROs = null;

            if (m_RealtimeQuery != null)
                m_RealtimeQuery.Destroy();

            if (AROPlacer != null)
                AROPlacer.PlacementCompleted -= AROPlaced;
        }

        void Start()
        {
            m_ParseClient = ParseManager.Instance.parseClient;
            m_ParseLiveClient = ParseManager.Instance.parseLiveClient;
        }

        public void Initialize()
        {
            m_GOs = new Dictionary<string, GameObject>();
            m_AROs = new Dictionary<string, ParseObject>();

            if (AROPlacer != null)
                AROPlacer.PlacementCompleted += AROPlaced;

            isInitialized = true;
        }

        private void OnDestroy()
        {
            if (m_RealtimeQuery != null)
                m_RealtimeQuery.Destroy();

            if (m_RealtimeQuery != null)
                m_RealtimeQuery.Destroy();

            if (AROPlacer != null)
                AROPlacer.PlacementCompleted -= AROPlaced;
        }

        public void Unsubscribe()
        {
            if (m_IsSubscribed)
            {
                foreach (GameObject go in m_GOs.Values)
                {
                    GameObject.Destroy(go);
                }
                m_GOs = new Dictionary<string, GameObject>();
                m_AROs = new Dictionary<string, ParseObject>();

                m_RealtimeQuery.OnCreate -= ARO_OnCreate;
                m_RealtimeQuery.OnDelete -= ARO_OnDelete;
                m_RealtimeQuery.OnEnter -= ARO_OnEnter;
                m_RealtimeQuery.OnLeave -= ARO_OnLeave;
                m_RealtimeQuery.OnUpdate -= ARO_OnUpdate;
                m_IsSubscribed = false;
            }
        }

        public void Subscribe()
        {
            if (!m_IsSubscribed)
            {
                m_RealtimeQuery.OnCreate += ARO_OnCreate;
                m_RealtimeQuery.OnDelete += ARO_OnDelete;
                m_RealtimeQuery.OnEnter += ARO_OnEnter;
                m_RealtimeQuery.OnLeave += ARO_OnLeave;
                m_RealtimeQuery.OnUpdate += ARO_OnUpdate;
                m_IsSubscribed = true;
            }
        }

        public async Task<ParseObject> AddScene(int mapId)
        {
            ParseObject scene = new ParseObject("SceneGlobal");
            scene["mapId"] = mapId;
            scene["AROs"] = new List<string>();
            await scene.SaveAsync();
            return scene;
        }

        public async Task<ParseObject> GetSceneByMapId(int mapId)
        {
            ParseQuery<ParseObject> query = m_ParseClient.GetQuery("SceneGlobal").WhereEqualTo("mapId", mapId);
            ParseObject scene = await query.FirstOrDefaultAsync();
            return scene;
        }

        public async Task<ParseObject> AddARO(IDictionary<string, object> data, int prefabIndex)
        {
            if (currentScene == null)
                return null;
            
            LocationProvider loc = LocationProvider.Instance;
            
            ParseObject aro = new ParseObject("AROGlobal");
            aro["sceneId"] = currentScene.ObjectId;
            aro["author"] = m_ParseClient.GetCurrentUser().Username;
            aro["prefabIndex"] = prefabIndex;
            aro["data"] = data;

            Transform cameraTransform = Camera.main.transform;
            LocalizerPose lastLocalizedPose = ImmersalSDK.Instance.Localizer.lastLocalizedPose;
            double vLatitude = 0;
            double vLongitude = 0;
            double vAltitude = 0;

            if (lastLocalizedPose.valid)
            {
                Matrix4x4 trackerSpace = Matrix4x4.TRS(cameraTransform.position + cameraTransform.forward, cameraTransform.rotation, Vector3.one);
                Matrix4x4 m = lastLocalizedPose.matrix * trackerSpace;
                Vector3 pos = m.GetColumn(3);

                double[] wgs84 = new double[3];
                int r = Immersal.Core.PosMapToWgs84(wgs84, pos, lastLocalizedPose.mapToEcef);
                vLatitude = wgs84[0];
                vLongitude = wgs84[1];
                vAltitude = wgs84[2];
            }

            aro["latitude"] = vLatitude;
            aro["longitude"] = vLongitude;
            aro["altitude"] = vAltitude;
            aro["quaternion_x"] = 0;
            aro["quaternion_y"] = 0;
            aro["quaternion_z"] = 0;
            aro["quaternion_w"] = 0;
            await aro.SaveAsync();

            currentScene.AddUniqueToList("AROs", aro.ObjectId);
            await currentScene.SaveAsync();

            //PlaceAROWithPlacer(aro.ObjectId);

            return aro;
        }

        public async void UpdateAROData(string id, IDictionary<string, object> newData)
        {
            ParseObject aro = m_AROs[id];
            aro["data"] = newData;
            await aro.SaveAsync();
        }

        public async void DeleteARO(string id)
        {
            ParseObject aro = m_AROs[id];
            await aro.DeleteAsync();

            currentScene.RemoveAllFromList("AROs", new List<string> { id });
            await currentScene.SaveAsync();
        }

        public async void DeleteAllAROs()
        {
            currentScene.RemoveAllFromList("AROs", m_AROs.Values);
            await currentScene.SaveAsync();
            await m_ParseClient.DeleteObjectsAsync(m_AROs.Values);
        }

        public async void MoveARO(string id, double[] coords)
        {
            ParseObject aro = m_AROs[id];
            aro["latitude"] = coords[0];
            aro["longitude"] = coords[1];
            aro["altitude"] = coords[2];
            await aro.SaveAsync();
        }

        public void PlaceAROWithPlacer(string uid)
        {
            m_GOs[uid]?.SetActive(false);
            m_UICanvas?.SetActive(false);

            if (AROPlacer?.CurrentState == AROPlacer.AROPlacerState.Off)
            {
                int ghostIndex = 0;
                ParseObject aro = m_AROs[uid];
                ghostIndex = aro.Get<int>("prefabIndex");

                AROPlacer.StartPlacing(uid, ghostIndex);
            }
        }
        
        public void PlaceARO(string uid, Pose worldPose)
        {
            AROPlaced(uid, worldPose);
        }

        public async void UpdateAROPose(string uid, double[] coords, double[] quaternion)
        {
            ParseObject aro = m_AROs[uid];

            aro["latitude"] = coords[0];
            aro["longitude"] = coords[1];
            aro["altitude"] = coords[2];
            aro["quaternion_x"] = quaternion[0];
            aro["quaternion_y"] = quaternion[1];
            aro["quaternion_z"] = quaternion[2];
            aro["quaternion_w"] = quaternion[3];
            await aro.SaveAsync();
        }

        /// <summary>
        /// Creates a realtime query and listens to the events
        /// </summary>
        public void StartRealtimeQuery()
        {
            Reset();
            Initialize();

            ParseQuery<ParseObject> query = m_ParseClient.GetQuery("AROGlobal").WhereEqualTo("sceneId", currentScene.ObjectId);

            m_GOs = new Dictionary<string, GameObject>();
            m_AROs = new Dictionary<string, ParseObject>();

            m_RealtimeQuery = new RealtimeQuery<ParseObject>(query, slowAndSafe: true);
            m_RealtimeQuery.OnCreate += ARO_OnCreate;
            m_RealtimeQuery.OnDelete += ARO_OnDelete;
            m_RealtimeQuery.OnEnter += ARO_OnEnter;
            m_RealtimeQuery.OnLeave += ARO_OnLeave;
            m_RealtimeQuery.OnUpdate += ARO_OnUpdate;
            m_IsSubscribed = true;
        }

        /// <summary>
        /// Something about one of our objects has been changed
        /// </summary>
        /// <param name="obj">the changed object</param>
        private void ARO_OnUpdate(ParseObject obj)
        {
            UpdateGO(obj);
        }

        /// <summary>
        /// We have had an object leave our query
        /// </summary>
        /// <param name="obj">the object that is leaving</param>
        private void ARO_OnLeave(ParseObject obj)
        {
            if (m_AROs.ContainsKey(obj.ObjectId))
                m_AROs.Remove(obj.ObjectId);
            
            DeleteGO(obj);
        }

        /// <summary>
        /// We have had a new object enter our query
        /// </summary>
        /// <param name="obj">the object that entered the query</param>
        private void ARO_OnEnter(ParseObject obj)
        {
            if (!m_AROs.ContainsKey(obj.ObjectId))
                m_AROs.Add(obj.ObjectId, obj);
            
            CreateGO(obj);
        }

        /// <summary>
        /// One of the objects we were looking at was deleted
        /// </summary>
        /// <param name="obj">the object that was deleted</param>
        private void ARO_OnDelete(ParseObject obj)
        {
            if (m_AROs.ContainsKey(obj.ObjectId))
                m_AROs.Remove(obj.ObjectId);
            
            DeleteGO(obj);
        }

        /// <summary>
        /// A new object has been created that matches the query
        /// </summary>
        /// <param name="obj">the object that was created</param>
        private void ARO_OnCreate(ParseObject obj)
        {
            if (!m_AROs.ContainsKey(obj.ObjectId))
                m_AROs.Add(obj.ObjectId, obj);
            
            CreateGO(obj);
        }

        /// <summary>
        /// Creates a new AR objects and updates its values as provided by the database
        /// </summary>
        /// <param name="arObject">the parse object retreived</param>
        private void CreateGO(ParseObject arObject)
        {
            AddNewGO(arObject);
            UpdateGO(arObject);
        }

        /// <summary>
        /// Delete the AR objects
        /// </summary>
        /// <param name="arObject">the object we want to delete</param>
        private void DeleteGO(ParseObject arObject)
        {
            if (m_GOs.ContainsKey(arObject.ObjectId))
            {
                GameObject.Destroy(m_GOs[arObject.ObjectId]);
                m_GOs.Remove(arObject.ObjectId);
            }
        }

        /// <summary>
        /// Updates the AR object based on the values provided
        /// </summary>
        /// <param name="arObject">the updated data</param>
        private void UpdateGO(ParseObject arObject)
        {
            double latitude = arObject.Get<double>("latitude");
            double longitude = arObject.Get<double>("longitude");
            double altitude = arObject.Get<double>("altitude");
            double[] quaternion = new double[4];
            quaternion[0] = arObject.Get<double>("quaternion_x");
            quaternion[1] = arObject.Get<double>("quaternion_y");
            quaternion[2] = arObject.Get<double>("quaternion_z");
            quaternion[3] = arObject.Get<double>("quaternion_w");
            GameObject arGO = m_GOs[arObject.ObjectId];

            ARODataHandler handler = arGO.GetComponent<ARODataHandler>();
            if (handler == null)
            {
                handler = arGO.AddComponent<ARODataHandler>();
            }

            handler.Uid = arObject.ObjectId;
            IDictionary<string, object> data = arObject.Get<IDictionary<string, object>>("data");
            handler.UpdateData(data);

            arGO.transform.localPosition = Wgs84PositionToUnity(latitude, longitude, altitude);
            arGO.transform.localRotation = Quaternion.identity; // TODO
            arGO.SetActive(true);
        }

        private Vector3 Wgs84PositionToUnity(double latitude, double longitude, double altitude)
        {
            Vector3 pos = default;
            if (currentMapId != -1 && ARSpace.mapIdToMap.ContainsKey(currentMapId))
            {
                ARMap map = ARSpace.mapIdToMap[currentMapId];
                double[] ecef = new double[3];
                double[] wgs84 = new double[3] { latitude, longitude, altitude };
                Core.PosWgs84ToEcef(ecef, wgs84);
                Core.PosEcefToMap(out pos, ecef, map.mapToEcef);
            }

            return pos;
        }

        private void AddNewGO(ParseObject aro)
        {
            if (m_GOs == null)
                return;

            if (m_GOs.ContainsKey(aro.ObjectId))
                return;

            int prefabIndex = 0;
            string url = "";
            IDictionary<string, object> data = aro.Get<IDictionary<string, object>>("data");

            switch (data["Prefab"])
            {
                case "Poster":
                    prefabIndex = 0;
                    url = data["PosterURL"] as string;
                    break;

                case "AR Diamond":
                    prefabIndex = 1;
                    break;

                default:
                    prefabIndex = 0;
                    break;
            }

            GameObject go;
            Transform cameraTransform = Camera.main.transform;

            if (prefabIndex >= 0 && prefabIndex < prefabs.Count)
            {
                Debug.LogFormat("[AROM] Instantiating prefab: {0}", prefabIndex);
                go = Instantiate(prefabs[prefabIndex], cameraTransform.position + cameraTransform.forward, Quaternion.identity, goContainer.transform);

                if (prefabIndex == 0)
                {
                    Immersal.Util.RemoteTexture rt = go.GetComponent<Immersal.Util.RemoteTexture>();
                    rt.url = url;
                }
            }
            else
            {
                Debug.Log("[AROM] Prefab index out of range. Instantiating default prefab.");
                go = Instantiate(defaultPrefab, cameraTransform.position + cameraTransform.forward, Quaternion.identity, goContainer.transform);
            }

            Debug.LogFormat("[AROM] Adding GO: {0}", aro.ObjectId);

            ARODataHandler handler = go.GetComponent<ARODataHandler>();
            if (handler == null)
            {
                handler = go.AddComponent<ARODataHandler>();
            }

            handler.Uid = aro.ObjectId;
            handler.CurrentData = data;

            if (!m_GOs.ContainsKey(aro.ObjectId))
                m_GOs.Add(aro.ObjectId, go);
        }

        public async void AddNewPoster(string url = "https://upload.wikimedia.org/wikipedia/commons/7/7b/Obverse_of_the_series_2009_%24100_Federal_Reserve_Note.jpg")
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["PosterURL"] = url;
            data["Prefab"] = "Poster";
            await AddARO(data, 0);
        }

        public async void AddNewDiamond()
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["Prefab"] = "AR Diamond";
            await AddARO(data, 1);
        }

        public async void AddNewObject()
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["Prefab"] = "AR Diamond";
            await AddARO(data, 1);
        }

        private void AROPlaced(string uid, Pose worldPose)
        {
            Vector3 localPos = goContainer.InverseTransformPoint(worldPose.position);
            Quaternion localRot = Quaternion.Inverse(goContainer.rotation) * worldPose.rotation;

            LocalizerPose lastLocalizedPose = ImmersalSDK.Instance.Localizer.lastLocalizedPose;
            double vLatitude = 0;
            double vLongitude = 0;
            double vAltitude = 0;

            if (lastLocalizedPose.valid)
            {
                Matrix4x4 trackerSpace = Matrix4x4.TRS(worldPose.position, worldPose.rotation, Vector3.one);
                Matrix4x4 m = lastLocalizedPose.matrix * trackerSpace;
                Vector3 pos = m.GetColumn(3);

                double[] wgs84 = new double[3];
                int r = Immersal.Core.PosMapToWgs84(wgs84, pos, lastLocalizedPose.mapToEcef);
                vLatitude = wgs84[0];
                vLongitude = wgs84[1];
                vAltitude = wgs84[2];
            }

            double[] coords = new double[3] { vLatitude, vLongitude, vAltitude };
            double[] rot = new double[4] { 0, 0, 0, 0 };    // TODO
            UpdateAROPose(uid, coords, rot);
            
            m_UICanvas?.SetActive(true);

            ARODataHandler handler = m_GOs[uid]?.GetComponent<ARODataHandler>();
            if (handler == null)
            {
                Debug.Log("[AROM] Cant find ARO data handler on GO. Adding new.");
                handler = m_GOs[uid]?.AddComponent<ARODataHandler>();
            }
            handler.AROMoved();
        }

        public void DeleteAllGOs()
        {
            if (m_GOs != null)
            {
                foreach (KeyValuePair<string, GameObject> kvp in m_GOs)
                {
                    if (kvp.Value != null)
                        Destroy(kvp.Value);
                }
                m_GOs.Clear();
            }
        }
    }
}
