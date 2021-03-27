/*===============================================================================
Copyright (C) 2020 Immersal Ltd. All Rights Reserved.

This file is part of the Immersal SDK.

The Immersal SDK cannot be copied, distributed, or made available to
third-parties for commercial purposes without written permission of Immersal Ltd.

Contact sdk@immersal.com for licensing requests.
===============================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;
using Immersal.AR;
using Immersal.REST;
using Immersal.Samples.DemoApp.ARO;
using Immersal.Samples.Mapping;
using Immersal.Samples.Util;

namespace Immersal.Samples.DemoApp
{
    public class DemoAppMapController : MonoBehaviour
    {
        private const int DefaultRadius = 50;
        private const int MinRadius = 1;
        private const int MaxRadius = 1000;
        private const int MaxNumberOfMaps = 8;
        private const string StatusSparse = "sparse";
        private const string StatusDone = "done";
        private const string StatusFailed = "failed";
        private const string StatusPending = "pending";
        private const string StatusProcessing = "processing";

        public TMP_Dropdown dropdown;
        public bool loadPublicMaps = false;

		[SerializeField]
        private GameObject m_ARSpace = null;
        
        private ImmersalSDK m_Sdk;
        private float startTime = 0;
        private bool m_HideStatusText = false;
        private List<SDKJob> m_ActiveMaps = new List<SDKJob>();
        private Dictionary<int, SDKJob> m_AllMaps = new Dictionary<int, SDKJob>();
        private LocalizerPose m_LastLocalizedPose = default;
        private bool m_IsCancelled = false;

        public List<SDKJob> maps
        {
            get { return m_AllMaps.Values.ToList(); }
        }

        void Awake()
        {

        }

        void OnEnable()
        {
            InitDropdown();

            startTime = Time.realtimeSinceStartup;

/*            if (m_Sdk != null && m_Jobs.Count == 0)
            {
                GetMaps();
            }*/

            ParseManager.Instance.parseLiveClient.OnConnected += OnParseConnected;
            m_IsCancelled = false;
        }

        void OnDisable()
        {
            ParseManager.Instance.parseLiveClient.OnConnected -= OnParseConnected;
            m_IsCancelled = true;
        }

        private void OnParseConnected()
        {
            m_HideStatusText = true;
        }

        private void InitDropdown(bool firstTime = false)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>() { "Loading map list..." });
        }

        private void UpdateDropdown()
        {
            List<string> names = new List<string>();

            foreach (SDKJob map in m_ActiveMaps)
            {
                names.Add(map.name);
            }

            int oldIndex = dropdown.value;
            if (oldIndex > names.Count - 1)
                oldIndex = 0;

            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>() { "Load map..." });
            dropdown.AddOptions(names);
            dropdown.SetValueWithoutNotify(oldIndex);
            dropdown.RefreshShownValue();
        }

        async void Start()
        {
            m_Sdk = ImmersalSDK.Instance;
            m_Sdk.Localizer.OnMapChanged += OnLocalizerMapChanged;
            m_Sdk.Localizer.OnPoseFound += OnPoseFound;

            await PollNearbyMaps();
        }

        void Update()
        {
            if (m_HideStatusText)
            {
                DemoAppManager.Instance.ShowStatusText(false);
                m_HideStatusText = false;
            }

            /*if (Time.realtimeSinceStartup - startTime >= 5f)
            {
                startTime = Time.realtimeSinceStartup;
                GetMaps();
            }

            if (m_JobLock == 1)
                return;

            if (m_Jobs.Count > 0)
            {
                m_JobLock = 1;
                RunJob(m_Jobs[0]);
            }*/
        }

        private async void OnLocalizerMapChanged(int mapId)
        {
            Debug.Log("   Map changed: " + mapId);
            if (mapId > 0)
            {
                Parse.ParseObject currentScene = await AROManagerGlobal.Instance.GetSceneByMapId(mapId);
                if (currentScene == null)
                {
                    currentScene = await AROManagerGlobal.Instance.AddScene(mapId);
                }
                Debug.Log("currentScene: " + currentScene.ObjectId);

                if (AROManagerGlobal.Instance.currentScene?.ObjectId != currentScene.ObjectId)
                {
                    AROManagerGlobal.Instance.currentScene = currentScene;
                    AROManagerGlobal.Instance.StartRealtimeQuery();

                    NotificationManager.Instance.GenerateNotification("Localized to map " + mapId);
                }
            }
        }

        private void OnPoseFound(LocalizerPose newPose)
        {
            m_LastLocalizedPose = newPose;
        }

        private void SortMaps()
        {
            // sort maps by distance
            JobDistanceComparer jc = new JobDistanceComparer();
            jc.deviceLatitude = LocationProvider.Instance.latitude;
            jc.deviceLongitude = LocationProvider.Instance.longitude;
            m_ActiveMaps.Sort(jc);
        }
        
        public async Task PollNearbyMaps()
        {
            while (true)
            {
                if (m_IsCancelled)
                    break;

                GetNearbyMaps();
                await Task.Delay(4000);
            }
        }

        public void LoadNearbyMaps()
        {
            // load max 8 nearby maps
            List<int> nearbyIds = new List<int>();
            for (var i = 0; i < m_ActiveMaps.Count; i++)
            {
                if (i == MaxNumberOfMaps) break;

                SDKJob job = m_ActiveMaps[i];
                nearbyIds.Add(job.id);
                if (!ARSpace.mapIdToMap.ContainsKey(job.id))
                {
                    LoadMap(job);
                }
            }

            // free old maps
            List<int> removals = new List<int>();
            foreach (int id in ARSpace.mapIdToMap.Keys)
            {
                if (!nearbyIds.Contains(id))
                {
                    removals.Add(id);
                }
            }
            foreach (int id in removals)
            {
                ARMap map = ARSpace.mapIdToMap[id];
                map.FreeMap();
                Destroy(map.gameObject);
            }
        }

        public async void GetNearbyMaps(bool load = true)
        {
            JobListJobsAsync j = new JobListJobsAsync();
            j.useGPS = LocationProvider.Instance.gpsOn;
            j.latitude = LocationProvider.Instance.latitude;
            j.longitude = LocationProvider.Instance.longitude;
            j.radius = DefaultRadius;
            j.OnResult += async (SDKJobsResult result) =>
            {
                m_AllMaps.Clear();
                m_ActiveMaps.Clear();

                if (result.count > 0)
                {
                    Debug.Log("Found " + result.count + " private maps");
                    // add private maps
                    foreach (SDKJob job in result.jobs)
                    {
                        m_AllMaps[job.id] = job;

//                        if (job.status == StatusSparse || job.status == StatusDone)
                        if (job.status != StatusFailed)
                        {
                            m_ActiveMaps.Add(job);
                        }
                    }
                }

                if (loadPublicMaps)
                {
                    JobListJobsAsync j2 = new JobListJobsAsync();
                    j2.useToken = false;
                    j2.useGPS = LocationProvider.Instance.gpsOn;
                    j2.latitude = LocationProvider.Instance.latitude;
                    j2.longitude = LocationProvider.Instance.longitude;
                    j2.radius = DefaultRadius;
                    j2.OnResult += async (SDKJobsResult result2) =>
                    {
                        if (result2.count > 0)
                        {
                            Debug.Log("Found " + result2.count + " public maps");
                            // add public maps
                            foreach (SDKJob job in result2.jobs)
                            {
                                m_AllMaps[job.id] = job;

//                                if (job.status == StatusSparse || job.status == StatusDone)
                                if (job.status != StatusFailed)
                                {
                                    m_ActiveMaps.Add(job);
                                }
                            }
                        }

                        SortMaps();
                        UpdateDropdown();

                        if (load)
                        {
                            //LocalizeGeoPose(GetMapIds());
                            m_Sdk.Localizer.mapIds = GetMapIds();
                            LoadNearbyMaps();
                        }
                    };

                    await j2.RunJobAsync();
                }
                else
                {
                    SortMaps();
                    UpdateDropdown();

                    if (load)
                    {
                        //LocalizeGeoPose(GetMapIds());
                        m_Sdk.Localizer.mapIds = GetMapIds();
                        LoadNearbyMaps();
                    }
                }
            };

            await j.RunJobAsync();
        }

        private SDKMapId[] GetMapIds()
        {
            SDKMapId[] mapIds = new SDKMapId[m_ActiveMaps.Count];
            for (int i = 0; i < m_ActiveMaps.Count; i++)
            {
                SDKMapId mapId = new SDKMapId();
                mapId.id = m_ActiveMaps[i].id;
                mapIds[i] = mapId;
            }

            return mapIds;
        }

        public void OnValueChanged(TMP_Dropdown dropdown)
        {
            int value = dropdown.value - 1;

            /*if (value >= 0)
            {
                SDKJob map = maps[value];
                switch (map.status)
                {
                    case StatusDone:
                    case StatusSparse:
                    {
                        m_ARMap.FreeMap();
                        LoadMap(map.id);
                    } break;
                    case StatusPending:
                    case StatusProcessing:
                        NotificationManager.Instance.GenerateWarning("The map hasn't finished processing yet, try again in a few seconds.");
                        dropdown.SetValueWithoutNotify(0);
                        break;
                    default:
                        break;
                }
            }*/
        }

        public async void LoadMap(SDKJob map)
        {
            if (!ParseManager.Instance.parseLiveClient.IsConnected())
                DemoAppManager.Instance.ShowStatusText(true, "Please wait while loading...");

            JobLoadMapAsync j = new JobLoadMapAsync();
            j.id = map.id;
            j.useToken = map.privacy == "0" ? true : false;
            j.OnResult += (SDKMapResult result) =>
            {
                byte[] mapData = Convert.FromBase64String(result.b64);
                Debug.Log(string.Format("Load map {0} ({1} bytes)", map.id, mapData.Length));

                Color pointCloudColor = new Color(221f, 255f, 25f) / 255;

                ARMap arMap = ARSpace.LoadAndInstantiateARMap(m_ARSpace.transform, map, mapData, ARMap.RenderMode.EditorAndRuntime, pointCloudColor);

                if (!m_Sdk.Localizer.autoStart)
                {
                    m_Sdk.Localizer.autoStart = true;
                    m_Sdk.Localizer.StartLocalizing();
                }
            };

            await j.RunJobAsync();
		}

        /*public async void LocalizeGeoPose(SDKMapId[] mapIds)
        {
            ARCameraManager cameraManager = m_Sdk.cameraManager;
            var cameraSubsystem = cameraManager.subsystem;

#if PLATFORM_LUMIN
            XRCameraImage image;
            if (cameraSubsystem.TryGetLatestImage(out image))
#else
            XRCpuImage image;
            if (cameraSubsystem.TryAcquireLatestCpuImage(out image))
#endif
            {
                JobGeoPoseAsync j = new JobGeoPoseAsync();

                byte[] pixels;
                Camera cam = Camera.main;
                Vector3 camPos = cam.transform.position;
                Quaternion camRot = cam.transform.rotation;
                int channels = 1;
                int width = image.width;
                int height = image.height;

                j.mapIds = mapIds;

                ARHelper.GetIntrinsics(out j.intrinsics);
                ARHelper.GetPlaneData(out pixels, image);

                Task<(byte[], icvCaptureInfo)> t = Task.Run(() =>
                {
                    byte[] capture = new byte[channels * width * height + 1024];
                    icvCaptureInfo info = Immersal.Core.CaptureImage(capture, capture.Length, pixels, width, height, channels);
                    Array.Resize(ref capture, info.captureSize);
                    return (capture, info);
                });

                await t;

                j.image = t.Result.Item1;

                j.OnResult += async (SDKGeoPoseResult result) =>
                {
                    if (result.success)
                    {
                        Debug.Log("*************************** GeoPose Localization Succeeded ***************************");
                        //this.stats.locSucc++;

                        int mapId = mapIds[0].id;
                        SDKJob job = m_AllMaps[mapId];
                        
                        double latitude = result.latitude;
                        double longitude = result.longitude;
                        double ellipsoidHeight = result.ellipsoidHeight;
                        Quaternion rot = new Quaternion(result.quaternion[1], result.quaternion[2], result.quaternion[3], result.quaternion[0]);
                        Debug.Log(string.Format("GeoPose returned latitude: {0}, longitude: {1}, ellipsoidHeight: {2}, quaternion: {3}", latitude, longitude, ellipsoidHeight, rot));

                        double[] ecef = new double[3];
                        double[] wgs84 = new double[3] { latitude, longitude, ellipsoidHeight };
                        Core.PosWgs84ToEcef(ecef, wgs84);

                        JobEcefAsync je = new JobEcefAsync();
                        je.id = mapId;
                        je.useToken = job.privacy == "0" ? true : false;
                        je.OnResult += (SDKEcefResult result2) =>
                        {
                            double[] mapToEcef = result2.ecef;
                            Vector3 mapPos;
                            Quaternion mapRot;
                            Core.PosEcefToMap(out mapPos, ecef, mapToEcef);
                            Core.RotEcefToMap(out mapRot, rot, mapToEcef);

                            Matrix4x4 cloudSpace = Matrix4x4.TRS(mapPos, mapRot, Vector3.one);
                            Matrix4x4 trackerSpace = Matrix4x4.TRS(camPos, camRot, Vector3.one);
                            Matrix4x4 m = trackerSpace*(cloudSpace.inverse);

                            LocalizerPose lastLocalizedPose;
                            LocalizerBase.GetLocalizerPose(out lastLocalizedPose, mapId, cloudSpace.GetColumn(3), cloudSpace.rotation, m.inverse, mapToEcef);
                            this.lastLocalizedPose = lastLocalizedPose;
                        };

                        await je.RunJobAsync();
                    }
                    else
                    {
                        //this.stats.locFail++;
                        Debug.Log("*************************** GeoPose Localization Failed ***************************");
                    }
                };

                await j.RunJobAsync();
                image.Dispose();
            }
        }*/
    }

    class JobDistanceComparer : IComparer<SDKJob> 
    { 
        public double deviceLatitude;
        public double deviceLongitude;

        public int Compare(SDKJob a, SDKJob b)
        {
            Vector2 pd = new Vector2((float)deviceLatitude, (float)deviceLongitude);
            Vector2 pa = new Vector2((float)a.latitude, (float)a.longitude);
            Vector2 pb = new Vector2((float)b.latitude, (float)b.longitude);
            double da = LocationUtil.DistanceBetweenPoints(pd, pa);
            double db = LocationUtil.DistanceBetweenPoints(pd, pb);

            return da.CompareTo(db);
        }
    }
}