using Silk.NET.Vulkan;
using System;
using System.Runtime.Serialization;
using Valve.VR;

namespace Ryujinx.Graphics.Vulkan
{
    class OpenVRException : Exception
    {
        public OpenVRException()
        {
        }

        public OpenVRException(EVRInitError error) : base($"Unexpected OpenVR Init error \"{error}\".")
        {
        }

        public OpenVRException(EVRApplicationError error) : base($"Unexpected OpenVR Application error \"{error}\".")
        {
        }

        public OpenVRException(EVRInputError error) : base($"Unexpected OpenVR Input error \"{error}\".")
        {
        }

        public OpenVRException(EVRCompositorError error) : base($"Unexpected OpenVR Compositor error \"{error}\".")
        {
        }

        public OpenVRException(string message) : base(message)
        {
        }

        public OpenVRException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected OpenVRException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
