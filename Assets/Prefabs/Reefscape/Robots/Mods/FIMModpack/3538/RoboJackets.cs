using Games.Reefscape.Enums;
using Games.Reefscape.GamePieceSystem;
using Games.Reefscape.Robots;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.GamePieceSystem;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using RobotFramework.GamePieceSystem;
using UnityEngine;
using System.Collections;
using Robots.Climbing;

namespace Prefabs.Reefscape.Robots.Mods.FIMModpack._3538
{
    public class RoboJackets : ReefscapeRobotBase
    {
        [Header("Components")]
        [SerializeField] private GenericElevator elevator;
        [SerializeField] private GenericJoint algaeArm;
        [SerializeField] private GenericJoint ClimberFoot;
        [SerializeField] private GenericJoint ClimberLatch;
        [SerializeField] private GenericJoint LeftPincer;
        [SerializeField] private GenericJoint RightPincer;

        [Header("Animation Joints (Wheels)")]
        [SerializeField] private GenericAnimationJoint[] intakeWheels;
        [SerializeField] private float wheelIntakeSpeed = -500f;

        [Header("PIDS")]
        [SerializeField] private PidConstants algaeArmPid;
        [SerializeField] private PidConstants ClimberFoodPid;
        [SerializeField] private PidConstants ClimberLatchPid;
        [SerializeField] private PidConstants LeftPincerPid;
        [SerializeField] private PidConstants RightPincerPid;

        [Header("Coral Setpoints")]
        [SerializeField] private RoboJacketsSetpoints stow;
        [SerializeField] private RoboJacketsSetpoints intake;
        [SerializeField] private RoboJacketsSetpoints l1;
        [SerializeField] private RoboJacketsSetpoints l1Place;
        [SerializeField] private RoboJacketsSetpoints l2;
        [SerializeField] private RoboJacketsSetpoints l3;
        [SerializeField] private RoboJacketsSetpoints l4;
        [SerializeField] private RoboJacketsSetpoints l4Place;

        [Header("Algae Setpoints")]
        [SerializeField] private RoboJacketsSetpoints groundAlgae;
        [SerializeField] private RoboJacketsSetpoints lollipopAlgae;
        [SerializeField] private RoboJacketsSetpoints processorSetpoint;
        [SerializeField] private RoboJacketsSetpoints lowAlgae;
        [SerializeField] private RoboJacketsSetpoints highAlgae;
        [SerializeField] private RoboJacketsSetpoints bargePlace;

        [Header("Intake Componenets")]
        [SerializeField] private ReefscapeGamePieceIntake coralIntake;
        [SerializeField] private ReefscapeGamePieceIntake algaeIntake;

        [Header("Game Piece States")]
        [SerializeField] private GamePieceState coralStowState;
        [SerializeField] private GamePieceState algaeStowState;

        [Header("Audio")]
        [SerializeField] private AudioSource algaeStallSource;
        [SerializeField] private AudioClip algaeStallAudio;
        [SerializeField] private AudioSource rollerSource;
        [SerializeField] private AudioClip intakeClip;
        [SerializeField] private BoxCollider coralTrigger;

        private OverlapBoxBounds soundDetector;
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _coralController;
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _algaeController;

        private float _elevatorTargetHeight;
        private float _algaeArmTargetAngle;
        private float _climbFootTargetAngle;
        private float _climbLatchTargetAngle;
        private float _leftPincerTargetAngle;
        private float _rightPincerTargetAngle;
        private LayerMask coralMask;
        private bool canClack;
        private bool _alreadyPlaced;
        private bool _isScoring = false; 
        private bool _isTransitioningToL1 = false; // Flag for L1 delay logic
        private ReefscapeSetpoints previousSetpoint = ReefscapeSetpoints.Stow;

        protected override void Start()
        {
            base.Start();
            algaeArm.SetPid(algaeArmPid);
            ClimberFoot.SetPid(ClimberFoodPid);
            ClimberLatch.SetPid(ClimberLatchPid);
            LeftPincer.SetPid(LeftPincerPid);
            RightPincer.SetPid(RightPincerPid);

            _climbFootTargetAngle = 70;
            _climbLatchTargetAngle = 80;
            _leftPincerTargetAngle = 0;
            _rightPincerTargetAngle = 0;

            RobotGamePieceController.SetPreload(coralStowState);
            _coralController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Coral.ToString());
            _algaeController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Algae.ToString());

            _coralController.gamePieceStates = new[] { coralStowState };
            _coralController.intakes.Add(coralIntake);
            _algaeController.gamePieceStates = new[] { algaeStowState };
            _algaeController.intakes.Add(algaeIntake);

            soundDetector = new OverlapBoxBounds(coralTrigger);
            coralMask = LayerMask.GetMask("Coral");

            rollerSource.clip = intakeClip;
            rollerSource.loop = true;
            rollerSource.Stop();

            algaeStallSource.clip = algaeStallAudio;
            algaeStallSource.loop = true;
            algaeStallSource.Stop();
        }

        private void LateUpdate()
        {
            algaeArm.UpdatePid(algaeArmPid);
            ClimberFoot.UpdatePid(ClimberFoodPid);
            ClimberLatch.UpdatePid(ClimberLatchPid);
            LeftPincer.UpdatePid(LeftPincerPid);
            RightPincer.UpdatePid(RightPincerPid);
        }

        private void FixedUpdate()
        {
            if (previousSetpoint == ReefscapeSetpoints.Place && CurrentSetpoint != ReefscapeSetpoints.Place)
            {
                _alreadyPlaced = false;
                _isTransitioningToL1 = false; // Reset transition flag when leaving Place state
            }

            bool hasAlgae = _algaeController.HasPiece();
            bool hasCoral = _coralController.HasPiece();

            _algaeController.SetTargetState(algaeStowState);
            _coralController.SetTargetState(coralStowState);

            // --- WHEEL ANIMATION LOGIC ---
            if (BaseGameManager.Instance.RobotState != RobotState.Disabled && !_isScoring)
            {
                if (IntakeAction.IsPressed())
                {
                    bool isAtAlgaeScoringPreset = CurrentSetpoint == ReefscapeSetpoints.HighAlgae || 
                                                 CurrentSetpoint == ReefscapeSetpoints.LowAlgae || 
                                                 CurrentSetpoint == ReefscapeSetpoints.Processor;

                    bool isGroundIntakingAlgae = CurrentRobotMode == ReefscapeRobotMode.Algae && 
                                                 CurrentSetpoint == ReefscapeSetpoints.Intake;

                    float direction = (isAtAlgaeScoringPreset || isGroundIntakingAlgae) ? -1f : 1f;
                    
                    foreach (var wheel in intakeWheels)
                        wheel.VelocityRoller(wheelIntakeSpeed * direction).useAxis(JointAxis.X);
                }
                else
                {
                    foreach (var wheel in intakeWheels)
                        wheel.VelocityRoller(0).useAxis(JointAxis.X);
                }
            }

            switch (CurrentSetpoint)
            {
                case ReefscapeSetpoints.Stow:
                    SetSetpoint(stow);
                    _algaeController.RequestIntake(algaeIntake, false);
                    _coralController.RequestIntake(coralIntake, false);
                    break;

                case ReefscapeSetpoints.Intake:
                    if (CurrentRobotMode == ReefscapeRobotMode.Algae && _algaeController.currentStateNum == 0 && !hasAlgae)
                    {
                        SetSetpoint(groundAlgae);
                        _algaeController.RequestIntake(algaeIntake, true);
                        _coralController.RequestIntake(coralIntake, false);
                    }
                    else
                    {
                        SetSetpoint(intake);
                        bool shouldIntakeCoral = (CurrentRobotMode == ReefscapeRobotMode.Coral || hasAlgae) && !hasCoral;
                        _coralController.RequestIntake(coralIntake, shouldIntakeCoral);

                        bool shouldIntakeAlgae = hasCoral && !hasAlgae;
                        _algaeController.RequestIntake(algaeIntake, shouldIntakeAlgae);

                        if (hasCoral && CurrentRobotMode != ReefscapeRobotMode.Coral) 
                        {
                            SetRobotMode(ReefscapeRobotMode.Coral);
                        }
                    }
                    break;

                case ReefscapeSetpoints.Place:
                    if (CurrentRobotMode == ReefscapeRobotMode.Algae)
                    {
                        if (LastSetpoint == ReefscapeSetpoints.L4 || LastSetpoint == ReefscapeSetpoints.Barge)
                            SetSetpoint(bargePlace);
                    }
                    else 
                    {
                        if (LastSetpoint == ReefscapeSetpoints.Barge) SetSetpoint(bargePlace);
                        else if (LastSetpoint == ReefscapeSetpoints.L4) SetSetpoint(l4Place);
                        else if (LastSetpoint == ReefscapeSetpoints.L3) SetSetpoint(l3); 
                        else if (LastSetpoint == ReefscapeSetpoints.L2) SetSetpoint(l2);
                        else if (LastSetpoint == ReefscapeSetpoints.L1 && !_isTransitioningToL1) 
                        {
                            StartCoroutine(L1DelayCoroutine());
                        }
                    }

                    if (OuttakeAction.triggered) StartCoroutine(PlaceCoroutine());
                    break;

                case ReefscapeSetpoints.L1: SetSetpoint(l1); break;
                case ReefscapeSetpoints.L2: SetSetpoint(l2); break;
                case ReefscapeSetpoints.L3: SetSetpoint(l3); break;
                case ReefscapeSetpoints.L4: SetSetpoint(l4); break;
                
                case ReefscapeSetpoints.Processor:
                    if (CurrentRobotMode == ReefscapeRobotMode.Algae) SetSetpoint(processorSetpoint);
                    else SetSetpoint(l1);
                    break;

                case ReefscapeSetpoints.LowAlgae:
                    SetSetpoint(lowAlgae);
                    _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed() && !hasAlgae);
                    break;

                case ReefscapeSetpoints.HighAlgae:
                    SetSetpoint(highAlgae);
                    _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed() && !hasAlgae);
                    break;
                case ReefscapeSetpoints.Stack:
                    SetSetpoint(lollipopAlgae);
                    _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed() && !hasAlgae);
                    break;
                case ReefscapeSetpoints.Barge:
                    SetSetpoint(bargePlace); 
                    break;
                case ReefscapeSetpoints.Climb:
                    _climbFootTargetAngle = 0;
                    _climbLatchTargetAngle = 0;
                    _leftPincerTargetAngle = 0;
                    _rightPincerTargetAngle = 0;
                    SetSetpoint(processorSetpoint);
                    break;
                case ReefscapeSetpoints.Climbed:
                    _climbFootTargetAngle = 0;
                    _climbLatchTargetAngle = 70;
                    _leftPincerTargetAngle = 90;
                    _rightPincerTargetAngle = -90;
                    break;
            }

            previousSetpoint = CurrentSetpoint;
            UpdateSetpoints();
            UpdateAudio();
        }

        private IEnumerator L1DelayCoroutine()
        {
            _isTransitioningToL1 = true;
            
            // Adjust the wait time here (seconds)
            yield return new WaitForSeconds(0.3f);

            // Double check we are still in Place and Mode is Coral before moving
            if (CurrentSetpoint == ReefscapeSetpoints.Place && CurrentRobotMode == ReefscapeRobotMode.Coral)
            {
                SetSetpoint(l1Place);
            }
            
            _isTransitioningToL1 = false;
        }

        private IEnumerator PlaceCoroutine()
        {
            if (_alreadyPlaced) yield break;
            
            _isScoring = true;
            PlaceGamePiece();

            float outtakeSpeed = wheelIntakeSpeed * 1f; 
            float animationDuration = 0.65f;
            float timer = 0;

            while (timer < animationDuration)
            {
                foreach (var wheel in intakeWheels)
                    wheel.VelocityRoller(outtakeSpeed).useAxis(JointAxis.X);
                
                timer += Time.deltaTime;
                yield return null;
            }

            foreach (var wheel in intakeWheels)
                wheel.VelocityRoller(0).useAxis(JointAxis.X);

            _isScoring = false;
        }

        private void PlaceGamePiece()
        {
            if (_alreadyPlaced) return;

            bool hasAlgae = _algaeController.HasPiece();
            bool hasCoral = _coralController.HasPiece();

            if (CurrentRobotMode == ReefscapeRobotMode.Coral && hasCoral) ExecuteCoralScore();
            else if (CurrentRobotMode == ReefscapeRobotMode.Algae && hasAlgae) ExecuteAlgaeScore();
            else if (hasCoral) ExecuteCoralScore();
            else if (hasAlgae) ExecuteAlgaeScore();
        }

        private void ExecuteCoralScore()
        {
            Vector3 force = new Vector3(0, 0, -3.5f);
            float time = 0.0f, maxSpeed = 0.5f;

            if (LastSetpoint == ReefscapeSetpoints.L4) { time = 0.5f; force = new Vector3(0, 0, -5f); }
            else if (LastSetpoint == ReefscapeSetpoints.L1) 
            { 
                time = 1f;
                force = new Vector3(0, 0, -4f);
            }
            else if (LastSetpoint == ReefscapeSetpoints.L2 || LastSetpoint == ReefscapeSetpoints.L3)
            {
                time = 0.15f; force = new Vector3(0, 0, -6.0f); maxSpeed = (LastSetpoint == ReefscapeSetpoints.L2) ? 0.6f : 0.4f;
            }

            _coralController.ReleaseGamePieceWithContinuedForce(force, time, maxSpeed);
            _alreadyPlaced = true;
        }

        private void ExecuteAlgaeScore()
        {
            Vector3 force = (LastSetpoint == ReefscapeSetpoints.L4 || LastSetpoint == ReefscapeSetpoints.Barge) 
                ? new Vector3(0, 0, 10f) : new Vector3(0, 10f, 0);

            _algaeController.ReleaseGamePieceWithForce(force);
            _alreadyPlaced = true;
        }

        private void SetSetpoint(RoboJacketsSetpoints setpoint)
        {
            _elevatorTargetHeight = setpoint.elevatorHeight;
            _algaeArmTargetAngle = setpoint.algaeArmAngle;
        }

        private void UpdateSetpoints()
        {
            elevator.SetTarget(_elevatorTargetHeight);
            algaeArm.SetTargetAngle(_algaeArmTargetAngle).withAxis(JointAxis.X);

            ClimberFoot.SetTargetAngle(_climbFootTargetAngle).withAxis(JointAxis.X);
            ClimberLatch.SetTargetAngle(_climbLatchTargetAngle).withAxis(JointAxis.X);
            LeftPincer.SetTargetAngle(_leftPincerTargetAngle).withAxis(JointAxis.Z);
            RightPincer.SetTargetAngle(_rightPincerTargetAngle).withAxis(JointAxis.Z);
        }

        private void UpdateAudio()
        {
            if (BaseGameManager.Instance.RobotState == RobotState.Disabled)
            {
                if (rollerSource.isPlaying || algaeStallSource.isPlaying)
                {
                    rollerSource.Stop();
                    algaeStallSource.Stop();
                }
                return;
            }

            bool isAttemptingIntake = IntakeAction.IsPressed() && !_coralController.HasPiece() && !_algaeController.HasPiece();
            bool isAttemptingOuttake = OuttakeAction.IsPressed();

            if ((isAttemptingIntake || isAttemptingOuttake) && !rollerSource.isPlaying)
            {
                rollerSource.Play();
            }
            else if (!IntakeAction.IsPressed() && !OuttakeAction.IsPressed() && rollerSource.isPlaying)
            {
                rollerSource.Stop();
            }
            else if (IntakeAction.IsPressed() && (_coralController.HasPiece() || _algaeController.HasPiece()))
            {
                rollerSource.Stop();
            }

            if (_algaeController.HasPiece() && !algaeStallSource.isPlaying)
            {
                algaeStallSource.Play();
            }
            else if (!_algaeController.HasPiece() && algaeStallSource.isPlaying)
            {
                algaeStallSource.Stop();
            }

            var a = soundDetector.OverlapBox(coralMask);
            if (a.Length > 0 && canClack)
            {
                canClack = false;
            }
            else if (a.Length == 0)
            {
                canClack = true;
            }
        }
    }
}