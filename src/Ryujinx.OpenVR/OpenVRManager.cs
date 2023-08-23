using Silk.NET.Vulkan;
using System;
using System.Linq;
using Valve.VR;
using System.IO;
using System.Reflection;
using System.Text;
using System.IO.MemoryMappedFiles;
using Ryujinx.Graphics.Vulkan;

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

        public static event EventHandler RenderLeftEye;
        public static event EventHandler RenderRightEye;

        private static CVRSystem _vrSystem;
        private static string _deviceExtensions = "";
        private static string _instanceExtensions = "";
        private static bool _evenFrame;
        private static float _fov = 104f;
        private static float _ipd = 0.0675f;

        private static Valve.VR.VRTextureBounds_t bounds = new VRTextureBounds_t() { uMin = 0, uMax = 1, vMin = 1, vMax = 0 };

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

            var inputError = Valve.VR.OpenVR.Input.SetActionManifestPath(Path.Combine(vrPath, "action_manifest.json"));

            if (inputError != EVRInputError.None)
                throw new OpenVRException(inputError);

            uint pnWidth = 0, pnHeight = 0;
            _vrSystem.GetRecommendedRenderTargetSize(ref pnWidth, ref pnHeight);

            StringBuilder pchValueExt = new StringBuilder(256);
            var dExtSize = Valve.VR.OpenVR.Compositor.GetVulkanDeviceExtensionsRequired(physicalDevice.Handle, pchValueExt, (uint)pchValueExt.Capacity);
            _deviceExtensions = pchValueExt.ToString();

            StringBuilder pchValueInst = new StringBuilder(256);
            var dInstSize = Valve.VR.OpenVR.Compositor.GetVulkanInstanceExtensionsRequired(pchValueInst, (uint)pchValueExt.Capacity);
            _instanceExtensions = pchValueExt.ToString();
        }

        public static void GetPoses()
        {
            var poses = new TrackedDevicePose_t[Valve.VR.OpenVR.k_unMaxTrackedDeviceCount];
            var gamePose = new TrackedDevicePose_t[0];

            Valve.VR.OpenVR.Compositor.WaitGetPoses(poses, gamePose);
        }

        public static void SubmitTextures(Texture_t vrTextureLeft, Texture_t vrTextureRight)
        {
            GetPoses();

            EVRCompositorError compositorError;

            compositorError = Valve.VR.OpenVR.Compositor.Submit(EVREye.Eye_Left, ref vrTextureLeft, ref bounds, EVRSubmitFlags.Submit_Default);

            if (compositorError != EVRCompositorError.None)
                throw new OpenVRException(compositorError);

            compositorError = Valve.VR.OpenVR.Compositor.Submit(EVREye.Eye_Right, ref vrTextureRight, ref bounds, EVRSubmitFlags.Submit_Default);

            if (compositorError != EVRCompositorError.None)
                throw new OpenVRException(compositorError);


            /*public void Present(CommandBufferScoped cbs, ITexture texture)
            {
                var poses = new TrackedDevicePose_t[Valve.VR.OpenVR.k_unMaxTrackedDeviceCount];
                var gamePose = new TrackedDevicePose_t[0];

                Valve.VR.OpenVR.Compositor.WaitGetPoses(poses, gamePose);

                var vrEvent = new VREvent_t();
                while (_vrSystem.PollNextEvent(ref vrEvent, (uint)sizeof(VREvent_t)))
                {

                }

                for (uint i = 0; i < Valve.VR.OpenVR.k_unMaxTrackedDeviceCount; i++)
                {
                    var state = new VRControllerState_t();
                    if (_vrSystem.GetControllerState(i, ref state, (uint)sizeof(VREvent_t)))
                    {

                    }
                }

                var mat = _vrSystem.GetProjectionMatrix(EVREye.Eye_Left, 10, 1000);

                var centerX = texture.Width / 2;
                var halfSize = _vrLeftTexture.Width / 2;
                //var centerY = texture.Height / 2;

                var src = new Extents2D(centerX - halfSize, 0, centerX + halfSize, texture.Height);
                var dst = new Extents2D(0, 0, _vrLeftTexture.Width, _vrLeftTexture.Height);

                if (_leftSide)
                    texture.CopyTo(_vrLeftTexture, src, dst, true);
                else
                    texture.CopyTo(_vrRightTexture, src, dst, true);

                _leftSide = !_leftSide;

                //_vrTexture.Storage.InsertWriteToReadBarrier(cbs, AccessFlags.TransferReadBit, PipelineStageFlags.TransferBit); //  PipelineStageFlags.FragmentShaderBit

                var vrLeftImage = _vrLeftTexture.GetImage().Get(cbs).Value;
                var vrRightImage = _vrRightTexture.GetImage().Get(cbs).Value;

                Valve.VR.VRTextureBounds_t bounds = new VRTextureBounds_t() { uMin = 0, uMax = 1, vMin = 1, vMax = 0 };

                Valve.VR.VRVulkanTextureData_t vrVKTextureData = new Valve.VR.VRVulkanTextureData_t() {
                    m_nImage = vrLeftImage.Handle, 
                    m_pDevice = _gd.Api.CurrentDevice.Value.Handle,
                    m_pPhysicalDevice = _physicalDevice.Handle, 
                    m_pInstance = _gd.Api.CurrentInstance.Value.Handle, 
                    m_pQueue = _gd.Queue.Handle,
                    m_nQueueFamilyIndex = _gd.QueueFamilyIndex,
                    m_nWidth = (uint)_vrLeftTexture.Width,
                    m_nHeight = (uint)_vrLeftTexture.Height,
                    m_nFormat = (uint)_vrLeftTexture.VkFormat,
                    m_nSampleCount = 1
                };

                Valve.VR.Texture_t vrTexture = new Valve.VR.Texture_t() { handle = (nint)(&vrVKTextureData), eType = ETextureType.Vulkan, eColorSpace = EColorSpace.Auto };

                EVRCompositorError compositorError;

                compositorError = Valve.VR.OpenVR.Compositor.Submit(EVREye.Eye_Left, ref vrTexture, ref bounds, EVRSubmitFlags.Submit_Default);

                if (compositorError != EVRCompositorError.None)
                    throw new OpenVRException(compositorError);

                vrVKTextureData.m_nImage = vrRightImage.Handle;

                compositorError = Valve.VR.OpenVR.Compositor.Submit(EVREye.Eye_Right, ref vrTexture, ref bounds, EVRSubmitFlags.Submit_Default);

                if (compositorError != EVRCompositorError.None)
                    throw new OpenVRException(compositorError);

            }*/
        }
    }
}
