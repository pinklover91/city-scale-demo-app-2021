/*===============================================================================
Copyright (C) 2020 Immersal Ltd. All Rights Reserved.

This file is part of the Immersal SDK.

The Immersal SDK cannot be copied, distributed, or made available to
third-parties for commercial purposes without written permission of Immersal Ltd.

Contact sdk@immersal.com for licensing requests.
===============================================================================*/

using UnityEngine;
using System.Collections;
using Immersal.AR;
using Immersal.Samples.Mapping;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

namespace Immersal.Samples.DemoApp
{
    public class LocationProvider : MonoBehaviour
    {
        public double latitude { get; private set; } = 0.0;
        public double longitude { get; private set; } = 0.0;
        public double altitude { get; private set; } = 0.0;
        public double vLatitude { get; private set; } = 0.0;
        public double vLongitude { get; private set; } = 0.0;
        public double vAltitude { get; private set; } = 0.0;
        public bool gpsOn
        {
#if (UNITY_IOS || PLATFORM_ANDROID) && !UNITY_EDITOR
            get { return NativeBindings.LocationServicesEnabled(); }
#else
            get
            {
                return Input.location.status == LocationServiceStatus.Running;
            }
#endif
        }

        public static LocationProvider Instance
        {
            get
            {
#if UNITY_EDITOR
                if (instance == null && !Application.isPlaying)
                {
                    instance = UnityEngine.Object.FindObjectOfType<LocationProvider>();
                }
#endif
                if (instance == null)
                {
                    Debug.LogError("No LocationProvider instance found. Ensure one exists in the scene.");
                }
                return instance;
            }
        }

        private static LocationProvider instance = null;

        void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            if (instance != this)
            {
                Debug.LogError("There must be only one LocationProvider object in a scene.");
                UnityEngine.Object.DestroyImmediate(this);
                return;
            }
        }

        private void Start()
        {
#if UNITY_IOS
            StartCoroutine(EnableLocationServices());
#elif PLATFORM_ANDROID
            if (Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Debug.Log("Location permission OK");
                StartCoroutine(EnableLocationServices());
            }
            else
            {
                Debug.Log("Requesting location permission");
                Permission.RequestUserPermission(Permission.FineLocation);
                StartCoroutine(WaitForLocationPermission());
            }
#endif
        }

        private void Update()
        {
            UpdateLocation();
        }

        private void UpdateLocation()
        {
            string s = "";

            if (gpsOn)
            {
#if (UNITY_IOS || PLATFORM_ANDROID) && !UNITY_EDITOR
                latitude = NativeBindings.GetLatitude();
                longitude = NativeBindings.GetLongitude();
                altitude = NativeBindings.GetAltitude();
#else
                latitude = Input.location.lastData.latitude;
                longitude = Input.location.lastData.longitude;
                altitude = Input.location.lastData.altitude;
#endif
            }

            LocalizerPose lastLocalizedPose = ImmersalSDK.Instance.Localizer.lastLocalizedPose;
            Camera cam = Camera.main;

            if (lastLocalizedPose.valid)
            {
/*                Vector2 cd = CompassDir(mainCamera, lastLocalizedPose.matrix, lastLocalizedPose.mapToEcef);
                float bearing = Mathf.Atan2(-cd.x, cd.y) * (180f / (float)Math.PI);
                if(bearing >= 0f)
                {
                    m_VBearing = bearing;
                }
                else
                {
                    m_VBearing = 360f - Mathf.Abs(bearing);
                }*/

                Matrix4x4 trackerSpace = Matrix4x4.TRS(cam.transform.position, cam.transform.rotation, Vector3.one);
                Matrix4x4 m = lastLocalizedPose.matrix * trackerSpace;
                Vector3 pos = m.GetColumn(3);

                double[] wgs84 = new double[3];
                int r = Immersal.Core.PosMapToWgs84(wgs84, pos, lastLocalizedPose.mapToEcef);
                vLatitude = wgs84[0];
                vLongitude = wgs84[1];
                vAltitude = wgs84[2];

                lastLocalizedPose.lastUpdatedPose.position = pos;
                lastLocalizedPose.lastUpdatedPose.rotation = m.rotation;
            }
        }

#if PLATFORM_ANDROID
        private IEnumerator WaitForLocationPermission()
        {
            while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                yield return null;
            }

            Debug.Log("Location permission OK");
            StartCoroutine(EnableLocationServices());
            yield return null;
        }
#endif

        private IEnumerator EnableLocationServices()
        {
            // First, check if user has location service enabled
            if (!Input.location.isEnabledByUser)
            {
                NotificationManager.Instance.GenerateNotification("Location services not enabled");
                Debug.Log("Location services not enabled");
                yield break;
            }

            // Start service before querying location
#if (UNITY_IOS || PLATFORM_ANDROID) && !UNITY_EDITOR
            NativeBindings.StartLocation();
#else
            Input.location.Start(0.001f, 0.001f);
#endif

            // Wait until service initializes
            int maxWait = 20;
#if (UNITY_IOS || PLATFORM_ANDROID) && !UNITY_EDITOR
            while (!NativeBindings.LocationServicesEnabled() && maxWait > 0)
#else
            while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
#endif
            {
                yield return new WaitForSeconds(1);
                maxWait--;
            }

            // Service didn't initialize in 20 seconds
            if (maxWait < 1)
            {
                NotificationManager.Instance.GenerateNotification("Location services timed out");
                Debug.Log("Timed out");
                yield break;
            }

            // Connection has failed
#if (UNITY_IOS || PLATFORM_ANDROID) && !UNITY_EDITOR
            if (!NativeBindings.LocationServicesEnabled())
#else
            if (Input.location.status == LocationServiceStatus.Failed)
#endif
            {
                NotificationManager.Instance.GenerateNotification("Unable to determine device location");
                Debug.Log("Unable to determine device location");
                yield break;
            }

#if (UNITY_IOS || PLATFORM_ANDROID) && !UNITY_EDITOR
            if (NativeBindings.LocationServicesEnabled())
#else
            if (Input.location.status == LocationServiceStatus.Running)
#endif
            {
                //NotificationManager.Instance.GenerateNotification("Tracking geolocation");
            }
        }
    }
}