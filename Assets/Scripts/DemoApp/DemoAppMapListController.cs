﻿/*===============================================================================
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
using TMPro;
using Immersal.AR;
using Immersal.REST;
using Immersal.Samples.DemoApp.ARO;
using Immersal.Samples.Mapping;
using Immersal.Samples.Util;

namespace Immersal.Samples.DemoApp
{
    [RequireComponent(typeof(TMP_Dropdown))]
    public class DemoAppMapListController : MonoBehaviour
    {
        [SerializeField]
        private ARMap m_ARMap = null;

        public Action MapListLoaded;

        private IDictionary<int, SDKJob> m_Maps;
        private TMP_Dropdown m_Dropdown;
        private List<Task> m_Jobs = new List<Task>();
        private int m_JobLock = 0;
        private ImmersalSDK m_Sdk;
        private TextAsset m_EmbeddedMap;
        private float startTime = 0;
        private bool m_HideStatusText = false;

        public TMP_Dropdown dropdown
        {
            get { return m_Dropdown; }
        }

        public List<SDKJob> maps
        {
            get { return m_Maps.Values.ToList(); }
        }

        void Awake()
        {
            m_Dropdown = GetComponent<TMP_Dropdown>();
            m_Maps = new Dictionary<int, SDKJob>();
        }

        void OnEnable()
        {
            InitDropdown(true);

            startTime = Time.realtimeSinceStartup;

            if (m_Sdk != null && m_Jobs.Count == 0)
            {
                GetMaps();
            }

            ParseManager.Instance.parseLiveClient.OnConnected += OnParseConnected;
        }

        void OnDisable()
        {
            m_Jobs.Clear();
            m_JobLock = 0;
            ParseManager.Instance.parseLiveClient.OnConnected -= OnParseConnected;
        }

        private void OnParseConnected()
        {
            m_HideStatusText = true;
        }

        private void InitDropdown(bool firstTime = false)
        {
            m_Dropdown.ClearOptions();

            if (firstTime)
            {
                m_Dropdown.AddOptions(new List<string>() { "Loading map list..." });
                return;
            }

            if (m_ARMap.mapFile != null)
            {
                m_Dropdown.AddOptions(new List<string>() { string.Format("<{0}>", m_ARMap.mapFile.name) });
                m_EmbeddedMap = m_ARMap.mapFile;
            }
            else
            {
                m_Dropdown.AddOptions(new List<string>() { "Load map..." });
            }
        }

        void Start()
        {
            m_Sdk = ImmersalSDK.Instance;
            GetMaps();
        }

        void Update()
        {
            if (m_HideStatusText)
            {
                DemoAppManager.Instance.ShowStatusText(false);
                m_HideStatusText = false;
            }

            if (m_Dropdown.IsExpanded && Time.realtimeSinceStartup - startTime >= 5f)
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
            }
        }

        public void OnValueChanged(TMP_Dropdown dropdown)
        {
            int value = dropdown.value - 1;

            // use embedded map
            if (m_EmbeddedMap != null && value == -1)
            {
                m_ARMap.mapFile = m_EmbeddedMap;
                m_ARMap.FreeMap();
                m_ARMap.LoadMap();
            }
            else
            {
                if (value >= 0)
                {
                    SDKJob map = maps[value];
                    switch (map.status)
                    {
                        case "done":
                        case "sparse":
                        {
                            m_ARMap.FreeMap();
                            LoadMap(map.id);
                        } break;
                        case "pending":
                        case "processing":
                            NotificationManager.Instance.GenerateWarning("The map hasn't finished processing yet, try again in a few seconds.");
                            dropdown.SetValueWithoutNotify(0);
                            break;
                        default:
                            break;
                    }
                }
            }
        }

        private List<SDKJob> GetMapsSorted()
        {
            List<SDKJob> sortedMaps = new List<SDKJob>();
            sortedMaps.AddRange(m_Maps.Values);
            // sort maps by date and distance
            JobDateDistanceComparer jc = new JobDateDistanceComparer();
            jc.deviceLatitude = LocationProvider.Instance.latitude;
            jc.deviceLongitude = LocationProvider.Instance.longitude;
            sortedMaps.Sort(jc);

            /*foreach (SDKJob j in sortedMaps)
            {
                Vector2 p1 = new Vector2((float)LocationProvider.Instance.latitude, (float)LocationProvider.Instance.longitude);
                Vector2 p2 = new Vector2((float)j.latitude, (float)j.longitude);
                double d = LocationUtil.DistanceBetweenPoints(p1, p2);
                Debug.Log("    Distance: " + d + ", created: " + DateTime.Parse(j.created));
            }*/
            return sortedMaps;
        }

        public void GetMaps()
        {
            JobListJobsAsync j = new JobListJobsAsync();
            j.useGPS = LocationProvider.Instance.gpsOn;
            j.latitude = LocationProvider.Instance.latitude;
            j.longitude = LocationProvider.Instance.longitude;
            j.radius = 200;
            j.OnResult += (SDKJobsResult result) =>
            {
                m_Maps.Clear();

                if (result.count > 0)
                {
                    // add private maps
                    foreach (SDKJob job in result.jobs)
                    {
                        if (job.status != "failed")
                        {
                            m_Maps[job.id] = job;
                        }
                    }
                }

                JobListJobsAsync j2 = new JobListJobsAsync();
                j2.useToken = false;
                j2.useGPS = LocationProvider.Instance.gpsOn;
                j2.latitude = LocationProvider.Instance.latitude;
                j2.longitude = LocationProvider.Instance.longitude;
                j2.radius = 200;
                j2.OnResult += (SDKJobsResult result2) =>
                {
                    if (result2.count > 0)
                    {
                        // add public maps
                        foreach (SDKJob job in result2.jobs)
                        {
                            if (job.status != "failed")
                            {
                                m_Maps[job.id] = job;
                            }
                        }
                    }

                    List<SDKJob> sortedMaps = GetMapsSorted();
                    List<string> names = new List<string>();

                    foreach (SDKJob map in sortedMaps)
                    {
                        names.Add(map.name);
                    }

                    int oldIndex = dropdown.value;
                    if (oldIndex > names.Count - 1)
                        oldIndex = 0;

                    InitDropdown();
                    m_Dropdown.AddOptions(names);
                    m_Dropdown.SetValueWithoutNotify(oldIndex);
                    m_Dropdown.RefreshShownValue();

                    MapListLoaded?.Invoke();
                };

                m_Jobs.Add(j2.RunJobAsync());
            };

            m_Jobs.Add(j.RunJobAsync());
        }

        public void LoadMap(int jobId)
        {
            if (!ParseManager.Instance.parseLiveClient.IsConnected())
                DemoAppManager.Instance.ShowStatusText(true, "Please wait while loading...");

            JobLoadMapAsync j = new JobLoadMapAsync();
            j.id = jobId;
            j.OnResult += async (SDKMapResult result) =>
            {
                byte[] mapData = Convert.FromBase64String(result.b64);
                Debug.Log(string.Format("Load map {0} ({1} bytes)", jobId, mapData.Length));

                this.m_ARMap.LoadMap(mapData);

                Parse.ParseObject currentScene = await AROManagerGlobal.Instance.GetSceneByMapId(jobId);
                if (currentScene == null)
                {
                    currentScene = await AROManagerGlobal.Instance.AddScene(jobId);
                }
                Debug.Log("currentScene: " + currentScene.ObjectId);

                AROManagerGlobal.Instance.currentScene = currentScene;
                AROManagerGlobal.Instance.StartRealtimeQuery();

                ARLocalizer.Instance.StartLocalizing();
                ARLocalizer.Instance.autoStart = true;
            };

            m_Jobs.Add(j.RunJobAsync());
        }
        
        private async void RunJob(Task t)
        {
            await t;
            if (m_Jobs.Count > 0)
            {
                m_Jobs.RemoveAt(0);
            }
            m_JobLock = 0;
        }
    }

    class JobDateDistanceComparer : IComparer<SDKJob> 
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
            DateTime dtA = Convert.ToDateTime(a.created);
            DateTime dtB = Convert.ToDateTime(b.created);
            
            int result = dtB.Date.CompareTo(dtA.Date);
            if (result == 0)
                result = da.CompareTo(db);
            return result;
        }
    } 
}
