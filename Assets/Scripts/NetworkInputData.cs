using Fusion;
using UnityEngine;

namespace Com.MyCompany.MyGame
{
    public struct NetworkInputData : INetworkInput
    {
        public Vector2 direction;
        public NetworkBool isInteractPressed;
        public float lookYaw;
        public NetworkButtons buttons;
    }

    public enum InputButton
    {
        Jump,
        Crouch,
        Sprint
    }
}
