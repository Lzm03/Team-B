using System.Collections;
using UnityEngine;
using Photon.Pun;
using Photon.Voice.Unity;
#if ENABLE_INPUT_SYSTEM 
using UnityEngine.InputSystem;
#endif

namespace CheeseController
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
public class CloneThirdPerson : MonoBehaviourPun, IPunObservable
{

        [Header("Player")] [Tooltip("Move speed of the character in m/s")]
        public float MoveSpeed = 2.0f;

        [Tooltip("How fast the character turns to face movement direction")] [Range(0.0f, 0.3f)]
        public float RotationSmoothTime = 0.12f;

        [Tooltip("Acceleration and deceleration")]
        public float SpeedChangeRate = 10.0f;

        [Space(10)] [Tooltip("The height the player can jump")]
        public float JumpHeight = 1.2f;

        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float Gravity = -15.0f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
        public float JumpTimeout = 0.50f;

        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
        public bool Grounded = true;

        [Tooltip("Useful for rough ground")] public float GroundedOffset = -0.14f;

        [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
        public float GroundedRadius = 0.28f;

        [Tooltip("What layers the character uses as ground")]
        public LayerMask GroundLayers;
        
        public ParticleSystem OnFireSystemPrefab;
    

        // player
        private float _speed;
        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        // timeout deltatime
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;
        
        public Vector3 currentPos;
        public Quaternion currentRot;

        private float _minLerpRate = 10f;
        private float _maxLerpRate = 20f;
        private float _lerpRate;
        private float _networkLatencyFactor;

        private Vector2 _lastMoveInput;
        private Vector2 _lastLookInput;
    
        private Transform _cachedTransform;

        private CheeseSmellController _cheeseSmellController;


#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
        private Animator _animator;
        private CharacterController _controller;
        private CheeseControllerInputs _input;
        private GameObject _mainCamera;
        private bool IsWalking = false;
        private const float _threshold = 0.01f;
        private bool _hasAnimator;
        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return _playerInput.currentControlScheme == "KeyboardMouse";
#else
				return false;
#endif
            }
        }


        private void Awake()
        {
            _input = GetComponent<CheeseControllerInputs>();
            // get a reference to our main camera
            if (_mainCamera == null)
            {
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            }
            _hasAnimator = TryGetComponent(out _animator);
            _cachedTransform = transform;
        }

        private void Start()
        {
            // _hasAnimator = TryGetComponent(out _animator);
            _controller = GetComponent<CharacterController>();
            // _input = GetComponent<CheeseControllerInputs>();
#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

            // AssignAnimationIDs();

            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;

            _cheeseSmellController = GetComponent<CheeseSmellController>();
        }

       

        private void Update()
        {
            _hasAnimator = TryGetComponent(out _animator);
            if(photonView.IsMine){
                JumpAndGravity();
                GroundedCheck();
                Move();
            }
            else
            {
                UpdateOther();
            }
        }

        void UpdateOther()
        {
            int currentPing = PhotonNetwork.GetPing();

            if (currentPing > 100)
            {
                _networkLatencyFactor = Mathf.InverseLerp(0, 200, currentPing);
                _lerpRate = Mathf.Lerp(_minLerpRate, _maxLerpRate, _networkLatencyFactor);
            }
            else
            {
                _lerpRate = _minLerpRate;
            }

            transform.position = Vector3.Lerp(transform.position, currentPos, Time.deltaTime * _lerpRate);
            transform.rotation = Quaternion.Slerp(transform.rotation, currentRot, Time.deltaTime * _lerpRate);
            
        }

        
        // private void GroundedCheck()
        // {
        //     // set sphere position, with offset
        //     Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
        //         transform.position.z);
        //     Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers,
        //         QueryTriggerInteraction.Ignore);
        // }

        private void GroundedCheck()
        {

            Vector3 rayStart = transform.position + (Vector3.up * 0.1f);

            float rayLength = Mathf.Abs(GroundedOffset) + 0.2f;
     
            Vector3 rayDirection = Vector3.down;

            Debug.DrawRay(rayStart, rayDirection * rayLength, Color.red);

            Grounded = Physics.Raycast(rayStart, rayDirection, rayLength, GroundLayers, QueryTriggerInteraction.Ignore);
        }

        private void Move()
        {
            // set target speed based on move speed, sprint speed and if sprint is pressed
            float targetSpeed = MoveSpeed;

            // a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

            // note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is no input, set the target speed to 0
            if (_input.move == Vector2.zero) targetSpeed = 0.0f;

            // a reference to the players current horizontal velocity
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // accelerate or decelerate to target speed
            if (currentHorizontalSpeed < targetSpeed - speedOffset ||
                currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                // creates curved result rather than a linear one giving a more organic speed change
                // note T in Lerp is clamped, so we don't need to clamp our speed
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                    Time.deltaTime * SpeedChangeRate);

                // round speed to 3 decimal places
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            // normalise input direction
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

            
            IsWalking = inputDirection.magnitude > 0;
            // note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is a move input rotate player when the player is moving
            if (_input.move != Vector2.zero)
            {
                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
                                  _mainCamera.transform.eulerAngles.y;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
                    RotationSmoothTime);
                    transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }


            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
            

            // move the player
            _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) +
                             new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

            // update animator if using character
            if (_hasAnimator)
            {
                _animator.SetBool("IsWalking", IsWalking);
            }
        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                // reset the fall timeout timer
                _fallTimeoutDelta = FallTimeout;

                // stop our velocity dropping infinitely when grounded
                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                // Jump - DISABLED
                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    
                    if (_hasAnimator)
                    {
                        _animator.SetTrigger("IsJumping");
                    }
                }

                // jump timeout - still count down to handle any existing logic dependencies, but jumping is disabled
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                // reset the jump timeout timer
                _jumpTimeoutDelta = JumpTimeout;

                // fall timeout
                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }

                // if we are not grounded, do not jump
                _input.jump = false; //- This line is now unnecessary as jumping is disabled
            }

            // apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            if (Grounded) Gizmos.color = transparentGreen;
            else Gizmos.color = transparentRed;

            // when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
            Gizmos.DrawSphere(
                new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z),
                GroundedRadius);
        }

        public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            if (stream.IsWriting)
            {
                bool positionChanged = Vector3.Distance(transform.position, currentPos) > 0.1f;
                bool rotationChanged = Quaternion.Angle(transform.rotation, currentRot) > 5.0f;
                bool moveChanged = _lastMoveInput != _input.move;
                bool lookChanged = _lastLookInput != _input.look;

                stream.SendNext(positionChanged);
                stream.SendNext(rotationChanged);
                stream.SendNext(moveChanged);
                stream.SendNext(lookChanged);

                if(positionChanged)
                {
                    stream.SendNext(transform.position);
                    currentPos = transform.position;
                }
                if(rotationChanged)
                {
                    stream.SendNext(transform.rotation);
                    currentRot = transform.rotation;
                }

                if(moveChanged)
                {
                    stream.SendNext(_input.move);
                    _lastMoveInput = _input.move;
                }
                if(lookChanged)
                {
                    stream.SendNext(_input.look);
                    _lastLookInput = _input.look;
                }
            }
            else
            {
                bool positionChanged = (bool)stream.ReceiveNext();
                bool rotationChanged = (bool)stream.ReceiveNext();
                bool moveChanged = (bool)stream.ReceiveNext();
                bool lookChanged = (bool)stream.ReceiveNext();

                if(positionChanged)
                {
                    currentPos = (Vector3)stream.ReceiveNext();
                }
                if(rotationChanged)
                {
                    currentRot = (Quaternion)stream.ReceiveNext();
                }
                if(moveChanged)
                {
                    _input.move = (Vector2)stream.ReceiveNext();
                }
                if(lookChanged)
                {
                    _input.look = (Vector2)stream.ReceiveNext();
                }
            }
        }
        
        public void endGame()
        {
            // 游戏结束，不让所有玩家移动
            gameObject.SetActive(false);
            _cheeseSmellController.setEnable(false);
        }
        
        [PunRPC]
        public void ActivateOnFireSystem()
        {
            OnFireSystemPrefab.gameObject.SetActive(true);
        }
        
        
        [PunRPC]
        public void DeactivateOnFireSystem()
        {
            OnFireSystemPrefab.gameObject.SetActive(false);
        }
    }
}