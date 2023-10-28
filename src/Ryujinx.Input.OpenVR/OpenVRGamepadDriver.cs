using Ryujinx.Common.Logging;
using Ryujinx.OpenVR;
using Ryujinx.SDL2.Common;
using System;
using System.Collections.Generic;
using System.Numerics;
using static SDL2.SDL;

namespace Ryujinx.Input.OpenVR
{
    public class OpenVRGamepadDriver : IGamepadDriver
    {
        private readonly Dictionary<int, string> _gamepadsInstanceIdsMapping;
        private readonly List<string> _gamepadsIds;

        public ReadOnlySpan<string> GamepadsIds => _gamepadsIds.ToArray();

        public string DriverName => "SDL2";

        public event Action<string> OnGamepadConnected;
        public event Action<string> OnGamepadDisconnected;

        private int virtualId;

        public OpenVRGamepadDriver()
        {
            _gamepadsInstanceIdsMapping = new Dictionary<int, string>();
            _gamepadsIds = new List<string>();

            SDL2Driver.Instance.Initialize();
            SDL2Driver.Instance.OnJoyStickConnected += HandleJoyStickConnected;
            SDL2Driver.Instance.OnJoystickDisconnected += HandleJoyStickDisconnected;

            virtualId = SDL_JoystickAttachVirtual((int)SDL_JoystickType.SDL_JOYSTICK_TYPE_GAMECONTROLLER, 6, 12, 0);
            OpenVRManager.UpdateVirtualJoystick += HandleVirtualJoyStickUpdate;

            // Add already connected gamepads
            int numJoysticks = SDL_NumJoysticks();

            for (int joystickIndex = 0; joystickIndex < numJoysticks; joystickIndex++)
            {
                HandleJoyStickConnected(joystickIndex, SDL_JoystickGetDeviceInstanceID(joystickIndex));
            }
        }

        private byte ConvertKey(bool state)
        {
            return (byte)(state ? 0x1 : 0x0);
        }

        static short Lerp(short min, short max, float value)
        {
            return (short)(min + ((max - min) * value));
        }

        private void HandleVirtualJoyStickUpdate(OpenVRManager.VRJoystick actions)
        {
            nint joystick = SDL_JoystickOpen(virtualId);

            int result = SDL_JoystickSetVirtualButton(joystick, (int)SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A, ConvertKey(actions.A));
            SDL_JoystickSetVirtualButton(joystick, (int)SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B, ConvertKey(actions.B));
            SDL_JoystickSetVirtualButton(joystick, (int)SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X, ConvertKey(actions.X));
            SDL_JoystickSetVirtualButton(joystick, (int)SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y, ConvertKey(actions.Y));

            SDL_JoystickSetVirtualButton(joystick, (int)SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START, ConvertKey(actions.Start));
            SDL_JoystickSetVirtualButton(joystick, (int)SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK, ConvertKey(actions.Select));

            SDL_JoystickSetVirtualAxis(joystick, (int)SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX, (short)(actions.LeftStick.X * short.MaxValue));
            SDL_JoystickSetVirtualAxis(joystick, (int)SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY, (short)(actions.LeftStick.Y * short.MaxValue));
            SDL_JoystickSetVirtualButton(joystick, (int)SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK, ConvertKey(actions.LeftStickPress));

            SDL_JoystickSetVirtualAxis(joystick, (int)SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX, (short)(actions.RightStick.X * short.MaxValue));
            SDL_JoystickSetVirtualAxis(joystick, (int)SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY, (short)(actions.RightStick.Y * short.MaxValue));
            SDL_JoystickSetVirtualButton(joystick, (int)SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK, ConvertKey(actions.RightStickPress));

            SDL_JoystickSetVirtualButton(joystick, (int)SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER, ConvertKey(actions.LeftBumper));
            SDL_JoystickSetVirtualAxis(joystick, (int)SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT, Lerp(short.MinValue, short.MaxValue, actions.LeftTrigger));
            //SDL_JoystickSetVirtualAxis(joystick, (int)SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT, (short)(actions.LeftTrigger * short.MaxValue));

            SDL_JoystickSetVirtualButton(joystick, (int)SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER, ConvertKey(actions.RightBumper));
            SDL_JoystickSetVirtualAxis(joystick, (int)SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT, Lerp(short.MinValue, short.MaxValue, actions.RightTrigger));
            //SDL_JoystickSetVirtualAxis(joystick, (int)SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT, (short)(actions.RightTrigger * short.MaxValue));

            SDL_JoystickClose(joystick);

            SDL_JoystickUpdate();
        }

        private static string GenerateGamepadId(int joystickIndex)
        {
            Guid guid = SDL_JoystickGetDeviceGUID(joystickIndex);

            if (guid == Guid.Empty)
            {
                return null;
            }

            return joystickIndex + "-" + guid;
        }

        private static int GetJoystickIndexByGamepadId(string id)
        {
            string[] data = id.Split("-");

            if (data.Length != 6 || !int.TryParse(data[0], out int joystickIndex))
            {
                return -1;
            }

            return joystickIndex;
        }

        private void HandleJoyStickDisconnected(int joystickInstanceId)
        {
            if (_gamepadsInstanceIdsMapping.TryGetValue(joystickInstanceId, out string id))
            {
                _gamepadsInstanceIdsMapping.Remove(joystickInstanceId);
                _gamepadsIds.Remove(id);

                OnGamepadDisconnected?.Invoke(id);
            }
        }

        private void HandleJoyStickConnected(int joystickDeviceId, int joystickInstanceId)
        {
            if (SDL_IsGameController(joystickDeviceId) == SDL_bool.SDL_TRUE)
            {
                string id = GenerateGamepadId(joystickDeviceId);

                if (id == null)
                {
                    return;
                }

                // Sometimes a JoyStick connected event fires after the app starts even though it was connected before
                // so it is rejected to avoid doubling the entries.
                if (_gamepadsIds.Contains(id))
                {
                    return;
                }

                if (_gamepadsInstanceIdsMapping.TryAdd(joystickInstanceId, id))
                {
                    _gamepadsIds.Add(id);

                    OnGamepadConnected?.Invoke(id);
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                SDL2Driver.Instance.OnJoyStickConnected -= HandleJoyStickConnected;
                SDL2Driver.Instance.OnJoystickDisconnected -= HandleJoyStickDisconnected;

                // Simulate a full disconnect when disposing
                foreach (string id in _gamepadsIds)
                {
                    OnGamepadDisconnected?.Invoke(id);
                }

                _gamepadsIds.Clear();

                SDL2Driver.Instance.Dispose();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        public IGamepad GetGamepad(string id)
        {
            int joystickIndex = GetJoystickIndexByGamepadId(id);

            if (joystickIndex == -1)
            {
                return null;
            }

            if (id != GenerateGamepadId(joystickIndex))
            {
                return null;
            }

            IntPtr gamepadHandle = SDL_GameControllerOpen(joystickIndex);

            if (gamepadHandle == IntPtr.Zero)
            {
                return null;
            }

            return new OpenVRGamepad(gamepadHandle, id);
        }
    }
}
