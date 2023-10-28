using Silk.NET.Vulkan;
using System;
using System.Linq;
using Valve.VR;
using System.IO;
using System.Reflection;
using System.Text;
using System.IO.MemoryMappedFiles;
using Ryujinx.Graphics.Vulkan;
using System.Numerics;
using System.Reflection.Metadata;
using Ryujinx.Common.Logging;

namespace Ryujinx.OpenVR
{
    public static unsafe class OpenVRManager
    {
        public static CVRSystem VRSystem => _vrSystem;
        public static string DeviceExtensions => _deviceExtensions;
        public static string InstanceExtensions => _instanceExtensions;
        public static bool EvenFrame { get => _evenFrame; set => _evenFrame = value; }
        public static float FOV => _fov;
        public static float IPD => _ipd;
        public static float ResScale { get => _resScale; set => _resScale = value; }
        public static bool FlatMode { get => _flatMode; }
        //public static nint GamepadId { get => _gamepadId; set => _gamepadId = value; }
        public static TrackedDevicePose_t HMDPose => _HMDPose;

        public static Vector3 RightPosition
        {
            get
            {
                if (_RightPose.bPoseIsValid)
                    return (_RightPose.mDeviceToAbsoluteTracking.GetPosition() - _HMDPose.mDeviceToAbsoluteTracking.GetPosition()) * new Vector3(-1, 1, 1);
                else
                    return Vector3.UnitZ + Vector3.UnitX;
            }
        }

        public static Quaternion RightRotation
        {
            get
            {
                if (_RightPose.bPoseIsValid)
                    return _RightPose.mDeviceToAbsoluteTracking.GetRotation();
                else
                    return Quaternion.Identity;
            }
        }

        public static event EventHandler RenderLeftEye;
        public static event EventHandler RenderRightEye;

        private static CVRSystem _vrSystem;
        private static CVROverlay _vrOverlay;
        private static string _deviceExtensions = "";
        private static string _instanceExtensions = "";
        private static bool _evenFrame;
        private static bool _lastEvenFrame;
        private static int _frameCount = 0;
        private static float _fov = 104f;
        private static float _ipd = 0.0675f;
        private static float _resScale;
        private static string _trackingSysName = "";
        private static string _modelNumber = "";
        private static string _manufacturerName = "";

        private static TrackedDevicePose_t _HMDPose;
        private static TrackedDevicePose_t _RightPose, _LeftPose;

        private static Valve.VR.VRTextureBounds_t _boundsL, _boundsR = new VRTextureBounds_t();

        private static ulong _menuOverlayHandle;
        private static bool _flatMode = true;

        // Controllers
        //private static nint _gamepadId = -1;
        private static VRActiveActionSet_t _activeActionSet;
        private static VRAction[] _vrActions;
        public static event Action<VRJoystick> UpdateVirtualJoystick;

        struct VRAction {
            public ulong handle;
            public bool analog;
        }

        public struct VRJoystick
        {
            public bool A;
            public bool B;
            public bool X;
            public bool Y;
            public bool Start;
            public bool Select;
            public Vector2 LeftStick;
            public bool LeftStickPress;
            public Vector2 RightStick;
            public bool RightStickPress;
            public bool LeftBumper;
            public float LeftTrigger;
            public bool RightBumper;
            public float RightTrigger;

        }

        public struct TrackedDevicePoseData
        {
            public string TrackedDeviceName;
            public Vector3 TrackedDevicePos;
            public Vector3 TrackedDeviceVel;
            public Vector3 TrackedDeviceAng;
            public Vector3 TrackedDeviceAngVel;
        };

        public static void SignalEvenFrame(bool even) {
            if (even) 
                RenderLeftEye?.Invoke(null, new EventArgs());
            else
                RenderRightEye?.Invoke(null, new EventArgs());
        }

        public static void Initialize(PhysicalDevice physicalDevice)
        {
            RenderLeftEye += (s, e) => _evenFrame = true;
            RenderRightEye += (s, e) => _evenFrame = false;

            var error = EVRInitError.None;
            _vrSystem = Valve.VR.OpenVR.Init(ref error, EVRApplicationType.VRApplication_Scene);

            if (error != EVRInitError.None)
                throw new OpenVRException(error);

            string currentDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string vrPath = Path.Combine(currentDir, "vr");

            var appError = Valve.VR.OpenVR.Applications.AddApplicationManifest(Path.Combine(vrPath, "manifest.vrmanifest"), false);

            if (appError != EVRApplicationError.None)
                throw new OpenVRException(appError);

            var inputError = Valve.VR.OpenVR.Input.SetActionManifestPath(Path.Combine(vrPath, "SteamVRActionManifest", "action_manifest.json"));

            if (inputError != EVRInputError.None)
                throw new OpenVRException(inputError);

            // Input
            _vrActions = new VRAction[] {
                GetAction("/actions/main/in/A", false),
                GetAction("/actions/main/in/B", false),
                GetAction("/actions/main/in/X", false),
                GetAction("/actions/main/in/Y", false),
                GetAction("/actions/main/in/Start", false),
                GetAction("/actions/main/in/Select", false),
                GetAction("/actions/main/in/LeftStick", true),
                GetAction("/actions/main/in/LeftStickPress", false),
                GetAction("/actions/main/in/RightStick", true),
                GetAction("/actions/main/in/RightStickPress", false),
                GetAction("/actions/main/in/LeftBumper", false),
                GetAction("/actions/main/in/LeftTrigger", true),
                GetAction("/actions/main/in/RightBumper", false),
                GetAction("/actions/main/in/RightTrigger", true),
            };

            ulong actionSet = 0; 
            Valve.VR.OpenVR.Input.GetActionSetHandle("/actions/main", ref actionSet);
            _activeActionSet = new VRActiveActionSet_t() { ulActionSet = actionSet, ulRestrictedToDevice = Valve.VR.OpenVR.k_ulInvalidInputValueHandle };

            // Tracking
            var propError = ETrackedPropertyError.TrackedProp_Success;

            StringBuilder pchValueManufacturerName = new StringBuilder(256);
            _vrSystem.GetStringTrackedDeviceProperty(Valve.VR.OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_ManufacturerName_String, pchValueManufacturerName, (uint)pchValueManufacturerName.Capacity, ref propError);
            _manufacturerName = pchValueManufacturerName.ToString();

            // Rendering          
            uint pnWidth = 0, pnHeight = 0;
            _vrSystem.GetRecommendedRenderTargetSize(ref pnWidth, ref pnHeight);

            StringBuilder pchValueExt = new StringBuilder(256);
            var dExtSize = Valve.VR.OpenVR.Compositor.GetVulkanDeviceExtensionsRequired(physicalDevice.Handle, pchValueExt, (uint)pchValueExt.Capacity);
            _deviceExtensions = pchValueExt.ToString();

            StringBuilder pchValueInst = new StringBuilder(256);
            var dInstSize = Valve.VR.OpenVR.Compositor.GetVulkanInstanceExtensionsRequired(pchValueInst, (uint)pchValueExt.Capacity);
            _instanceExtensions = pchValueExt.ToString();

            float l_left = 0.0f, l_right = 0.0f, l_top = 0.0f, l_bottom = 0.0f;
            _vrSystem.GetProjectionRaw(EVREye.Eye_Left, ref l_left, ref l_right, ref l_top, ref l_bottom);

            float r_left = 0.0f, r_right = 0.0f, r_top = 0.0f, r_bottom = 0.0f;
            _vrSystem.GetProjectionRaw(EVREye.Eye_Right, ref r_left, ref r_right, ref r_top, ref r_bottom);

            float tanHalfFovU = MathF.Max(MathF.Max( -l_left, l_right), MathF.Max(-r_left, r_right));
            float tanHalfFovV = MathF.Max(MathF.Max( -l_top, l_bottom), MathF.Max(-r_top, r_bottom));

            _boundsL.uMin = 0.5f + 0.5f * l_left / tanHalfFovU;
            _boundsL.uMax = 0.5f + 0.5f * l_right / tanHalfFovU;
            _boundsL.vMin = 0.5f - 0.5f * l_top / tanHalfFovV; 
            _boundsL.vMax = 0.5f - 0.5f * l_bottom / tanHalfFovV;

            _boundsR.uMin = 0.5f + 0.5f * r_left / tanHalfFovU;
            _boundsR.uMax = 0.5f + 0.5f * r_right / tanHalfFovU;
            _boundsR.vMin = 0.5f - 0.5f * r_top / tanHalfFovV;
            _boundsR.vMax = 0.5f - 0.5f * r_bottom / tanHalfFovV;

            _fov = 2f * MathF.Atan(tanHalfFovU) * 360 / (MathF.PI * 2f);

            _vrOverlay = Valve.VR.OpenVR.Overlay;

            _vrOverlay.CreateOverlay("MenuOverlayKey", "MenuOverlay", ref _menuOverlayHandle);
            _vrOverlay.SetOverlayInputMethod(_menuOverlayHandle, VROverlayInputMethod.Mouse);
            _vrOverlay.SetOverlayCurvature(_menuOverlayHandle, 0.15f);

            var mouseScale = new HmdVector2_t { v0 = 1920, v1 = 1080 };
            _vrOverlay.SetOverlayMouseScale(_menuOverlayHandle, ref mouseScale);

            _vrOverlay.SetOverlayFlag(_menuOverlayHandle, VROverlayFlags.IgnoreTextureAlpha, true);
            _vrOverlay.SetOverlayTexelAspect(_menuOverlayHandle, 1920f / 1080f);

            var bounds = new VRTextureBounds_t { uMin = 0, vMin = 1, uMax = 1, vMax = 0 };
            _vrOverlay.SetOverlayTextureBounds(_menuOverlayHandle, ref bounds);
        }

        static VRAction GetAction(string actionName, bool analog=false) {
            ulong handle = 0;
            EVRInputError error =  Valve.VR.OpenVR.Input.GetActionHandle(actionName, ref handle);

            if (error != EVRInputError.None)
                throw new OpenVRException(error);

            return new VRAction { handle = handle, analog = analog };
        }

        public static uint EncodeFloat(float value)
        {
            return BitConverter.ToUInt32(BitConverter.GetBytes(value));
        }

        private static void GetPoseData(TrackedDevicePose_t poseRaw, TrackedDevicePoseData poseOut)
        {
            if (poseRaw.bPoseIsValid)
            {
                HmdMatrix34_t mat = poseRaw.mDeviceToAbsoluteTracking;

                poseOut.TrackedDevicePos = mat.GetPosition();
                poseOut.TrackedDeviceVel = new Vector3(poseRaw.vVelocity.v0, poseRaw.vVelocity.v1, -poseRaw.vVelocity.v2);
                poseOut.TrackedDeviceAng = ToEulerAngles(mat.GetRotation());
                poseOut.TrackedDeviceAngVel = new Vector3(poseRaw.vVelocity.v0, poseRaw.vVelocity.v1, -poseRaw.vVelocity.v2) * (180.0f / 3.141592654f);
            }
        }

        public static Vector3 ToEulerAngles(Quaternion q)
        {
            Vector3 angles = Vector3.Zero;

            // roll (x-axis rotation)
            float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
            float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            angles.Z = MathF.Atan2(sinr_cosp, cosr_cosp);

            // pitch (y-axis rotation)
            float sinp = 2 * (q.W * q.Y - q.Z * q.X);
            if (Math.Abs(sinp) >= 1)
            {
                angles.X = MathF.CopySign(MathF.PI / 2, sinp);
            }
            else
            {
                angles.X = MathF.Asin(sinp);
            }

            // yaw (z-axis rotation)
            float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            angles.Y = MathF.Atan2(siny_cosp, cosy_cosp);

            return angles;
        }

        public static void GetPoses()
        {
            var poses = new TrackedDevicePose_t[Valve.VR.OpenVR.k_unMaxTrackedDeviceCount];
            var gamePose = new TrackedDevicePose_t[0];

            Valve.VR.OpenVR.Compositor.WaitGetPoses(poses, gamePose);

            _HMDPose = poses[Valve.VR.OpenVR.k_unTrackedDeviceIndex_Hmd];

            var leftID = _vrSystem.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);

            if (poses.Length > leftID)
                _LeftPose = poses[leftID];

            var rightID = _vrSystem.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);

            if (poses.Length > rightID)
                _RightPose = poses[rightID];
        }

        private static void ProcessInput()
        {
            EVRInputError error = Valve.VR.OpenVR.Input.UpdateActionState(new VRActiveActionSet_t[] { _activeActionSet }, (uint)sizeof(Valve.VR.VRActiveActionSet_t));

            if (error != EVRInputError.None)
                throw new OpenVRException(error);

            VRJoystick vRJoystick = new VRJoystick();

            InputDigitalActionData_t digitalData = new InputDigitalActionData_t();
            InputAnalogActionData_t analogData = new InputAnalogActionData_t();

            for (int i = 0; i < _vrActions.Length; i++)
            {
                var action = _vrActions[i];

                if (action.analog)
                {                  
                    error = Valve.VR.OpenVR.Input.GetAnalogActionData(action.handle, ref analogData, (uint)sizeof(InputAnalogActionData_t), Valve.VR.OpenVR.k_ulInvalidInputValueHandle);
                    
                    if (analogData.deltaX != 0 || analogData.deltaY != 0)
                        Logger.Info?.Print(LogClass.VR, $"ProcessInput - Analog {i}: {analogData.x}, {analogData.y} ({error})");
                }
                else
                {                   
                    error = Valve.VR.OpenVR.Input.GetDigitalActionData(action.handle, ref digitalData, (uint)sizeof(InputDigitalActionData_t), Valve.VR.OpenVR.k_ulInvalidInputValueHandle);

                    if (digitalData.bChanged) // error != EVRInputError.NoData
                        Logger.Info?.Print(LogClass.VR, $"ProcessInput - Digital {i}: {digitalData.bState} ({error})");
                }

                if (error != EVRInputError.None)
                    continue;

                switch (i)
                {
                    case 0:
                        vRJoystick.A = digitalData.bState;
                        break;
                    case 1:
                        vRJoystick.B = digitalData.bState;
                        break;
                    case 2:
                        vRJoystick.X = digitalData.bState;
                        break;
                    case 3:
                        vRJoystick.Y = digitalData.bState;
                        break;
                    case 4:
                        vRJoystick.Start = digitalData.bState;
                        break;
                    case 5:
                        vRJoystick.Select = digitalData.bState;
                        break;
                    case 6:
                        vRJoystick.LeftStick = new Vector2(analogData.x, -analogData.y);
                        break;
                    case 7:
                        vRJoystick.LeftStickPress = digitalData.bState;
                        break;
                    case 8:
                        vRJoystick.RightStick = new Vector2(analogData.x, -analogData.y);
                        break;
                    case 9:
                        vRJoystick.RightStickPress = digitalData.bState;
                        break;
                    case 10:
                        vRJoystick.LeftBumper = digitalData.bState;
                        break;
                    case 11:
                        vRJoystick.LeftTrigger = analogData.x;
                        break;
                    case 12:
                        vRJoystick.RightBumper = digitalData.bState;
                        break;
                    case 13:
                        vRJoystick.RightTrigger = analogData.x;
                        break;
                }
            }

            UpdateVirtualJoystick(vRJoystick);
        }

        public static void RepositionOverlays()
        {
            var hmdMat = _HMDPose.mDeviceToAbsoluteTracking;

            var hmdPosition = hmdMat.GetPosition();
            var hmdForward = new Vector3(-hmdMat.m2, 0, -hmdMat.m10);

            var menuTransform = new HmdMatrix34_t {
                m0 = 1.0f, m1 = 0.0f, m2 = 0.0f, m3 = 0.0f,
                m4 = 0.0f, m5 = 1.0f, m6 = 0.0f, m7 = 1.0f,
                m8 = 0.0f, m9 = 0.0f, m10 = 1.0f, m11 = 1.0f
            };

            var trackingOrigin = Valve.VR.OpenVR.Compositor.GetTrackingSpace();

            /*float widthRatio = windowWidth / renderWidth;
            float heightRatio = windowHeight / renderHeight;
            menuTransform.m0 *= widthRatio;
            menuTransform.m5 *= heightRatio;*/

            /*menuTransform.m0 = 3;
            menuTransform.m5 = 3f * (1080f / 1920f);*/

            var overlayPosition = hmdPosition + (hmdForward * 3);

            menuTransform.SetPosition(overlayPosition);

            float xScale = menuTransform.m0;
            float hmdRotationDegrees = MathF.Atan2(hmdMat.m2, hmdMat.m10);

            menuTransform.m0 *= MathF.Cos(hmdRotationDegrees);
            menuTransform.m2 = MathF.Sin(hmdRotationDegrees);
            menuTransform.m8 = -MathF.Sin(hmdRotationDegrees) * xScale;
            menuTransform.m10  *= MathF.Cos(hmdRotationDegrees);

            _vrOverlay.SetOverlayTransformAbsolute(_menuOverlayHandle, trackingOrigin, ref menuTransform);
            _vrOverlay.SetOverlayWidthInMeters(_menuOverlayHandle, 4);
        }

        public static void SubmitTextures(Texture_t emptyTexture, Texture_t vrTextureLeft, Texture_t vrTextureRight, Texture_t overlayTexture)
        {
            if (_evenFrame == _lastEvenFrame)
                _frameCount++;
            else
            {
                _lastEvenFrame = _evenFrame;
                _frameCount = 0;
            }

            if (_frameCount > 3)
                _flatMode = true;
            else
                _flatMode = false;

            GetPoses();
            ProcessInput();

            var isVisible = _vrOverlay.IsOverlayVisible(_menuOverlayHandle);

            Texture_t localLeft = vrTextureLeft, localRight = vrTextureRight;

            if (_flatMode)
            {
                if (!isVisible)
                {
                    RepositionOverlays();
                    _vrOverlay.ShowOverlay(_menuOverlayHandle);
                }

                _vrOverlay.SetOverlayTexture(_menuOverlayHandle, ref vrTextureRight);
                
                localLeft = emptyTexture;
                localRight = emptyTexture;
            }
            else
            {
                if (isVisible)
                    _vrOverlay.HideOverlay(_menuOverlayHandle);
            }

            EVRCompositorError compositorError;

            compositorError = Valve.VR.OpenVR.Compositor.Submit(EVREye.Eye_Left, ref localLeft, ref _boundsL, EVRSubmitFlags.Submit_Default);

            if (compositorError != EVRCompositorError.None)
                throw new OpenVRException(compositorError);

            compositorError = Valve.VR.OpenVR.Compositor.Submit(EVREye.Eye_Right, ref localRight, ref _boundsR, EVRSubmitFlags.Submit_Default);

            if (compositorError != EVRCompositorError.None)
                throw new OpenVRException(compositorError);
        }
    }
}
