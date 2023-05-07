//===================================================
// FileName:      ThridPersonController.cs         
// Author:        Allent Lee	
// CreateTime:    2023-05-07 00:48:21	
// E-mail:        xiaomo_lzm@163.com
// Description:   
//===================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;



public class ThirdPersonCtrl : MonoBehaviour
{
    private Transform playerTransform;
    private Transform cameraTransform;
    private Animator animator;
    
    // private AnimatorStateInfo animState;    //动画状态
    public Transform rightHandObject;           //刀柄IK位置
    
    //玩家姿态
    public enum PlayerPosture
    {
        Crouch,     //下蹲
        Stand,      //站立
        Midair      //滞空
    }

//运动状态
    public enum LocomotionState
    {
        Idle,
        Walk,
        Run
    }

//战斗状态
    public enum AttackState
    {
        Normal,
        Attack
    }
    
    [HideInInspector]
    public PlayerPosture playerPosture = PlayerPosture.Stand;
    private const float crouchThreshold = 0f;   //和状态机里空手运动BlendTree的阈值一致
    private const float standThreshold = 1f;
    private const float midairThreshold = 2f;

    [HideInInspector]
    public LocomotionState locomotionState = LocomotionState.Idle;
    [HideInInspector]
    public AttackState attackState = AttackState.Attack;
    private const float crouchSpeed = 2f;   //蹲着走速度
    private const float walkSpeed = 4f;
    private const float runSpeed = 8f;

    private Vector2 MoveInput;
    private bool isRunning;
    private bool isCrouch;
    private bool isJumping;
    private bool isAttacking;

    private int postureHash;    //动画状态机玩家姿态参数哈希值，使用哈希值比直接使用字符串效率高
    private int moveSpeedHash;
    private int turnSpeedHash;
    
    Vector3 playerMovement = Vector3.zero;  //玩家实际要移动的方向
    
    // Start is called before the first frame update
    void Start()
    {
        playerTransform = this.transform;   //提升访问效率，避免每次都去访问组件
        cameraTransform = Camera.main.transform;
        animator = GetComponent<Animator>();
        // animState = animator.GetCurrentAnimatorStateInfo(animator.GetLayerIndex("Base Layer"));
        postureHash = Animator.StringToHash("玩家姿态");
        moveSpeedHash = Animator.StringToHash("移动速度");
        turnSpeedHash = Animator.StringToHash("转弯速度");
    }

    // Update is called once per frame
    void Update()
    {
        CaculateInputDirection();
        SwitchPlayerStates();
        SetupAnimator();
    }

    #region 输入相关
    public void GetMoveInput(InputAction.CallbackContext ctx)
    {
        MoveInput = ctx.ReadValue<Vector2>();
    }
    public void GetRunInput(InputAction.CallbackContext ctx)
    {
        isRunning = ctx.ReadValueAsButton();
    }
    public void GetCrouchInput(InputAction.CallbackContext ctx)
    {
        isCrouch = ctx.ReadValueAsButton();
    }
    public void GetJumpkInput(InputAction.CallbackContext ctx)
    {
        isJumping = ctx.ReadValueAsButton();
    }
    public void GetAttackInput(InputAction.CallbackContext ctx)
    {
        isAttacking = ctx.ReadValueAsButton();
    }
    #endregion
    
    private void OnAnimatorIK(int layerIndex)
    {
        animator.SetIKPosition(AvatarIKGoal.RightHand, rightHandObject.position);
        animator.SetIKRotation(AvatarIKGoal.RightHand, rightHandObject.rotation);
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand,1f);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand,1f);
    }
    
    //切换玩家状态
    void SwitchPlayerStates()
    {
        if (isCrouch)
        {
            playerPosture = PlayerPosture.Crouch;
        }
        else if (isJumping)
        {
            playerPosture = PlayerPosture.Midair;
        }
        else
        {
            playerPosture = PlayerPosture.Stand;
        }

        if (MoveInput.magnitude == 0)   //玩家没输入
        {
            locomotionState = LocomotionState.Idle;
        }
        else if (!isRunning)
        {
            locomotionState = LocomotionState.Walk;
        }
        else
        {
            locomotionState = LocomotionState.Run;
        }

        if (isAttacking)
        {
            attackState = AttackState.Attack;
        }
        else
        {
            attackState = AttackState.Normal;
        }
    }
    
    //计算移动方向
    void CaculateInputDirection()
    {
        Vector3 camForwawrdProjection = new Vector3(MoveInput.x, 0, MoveInput.y);
        float y = cameraTransform.rotation.eulerAngles.y;
        camForwawrdProjection = Quaternion.Euler(0, y, 0) * camForwawrdProjection;
        // Vector3 camForwawrdProjection = new Vector3(cameraTransform.forward.x, 0, cameraTransform.forward.z).normalized;//相机在水平方向的投影
        // playerMovement = MoveInput.y * camForwawrdProjection + MoveInput.x * cameraTransform.right;

        playerMovement = playerTransform.InverseTransformVector(playerMovement);   //将世界坐标转为本地坐标
    }

    //设置动画参数
    void SetupAnimator()
    {
        if (playerPosture == PlayerPosture.Stand)
        {
            animator.SetFloat(postureHash, standThreshold, 0.1f, Time.deltaTime);
            switch (locomotionState)
            {
                case LocomotionState.Idle:
                    animator.SetFloat(moveSpeedHash, 0, 0.1f, Time.deltaTime);
                    break;
                case LocomotionState.Walk:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * walkSpeed, 0.1f, Time.deltaTime);
                    break;
                case LocomotionState.Run:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * runSpeed, 0.1f, Time.deltaTime);
                    break;
            }
        }
        else if (playerPosture == PlayerPosture.Crouch)
        {
            animator.SetFloat(postureHash, crouchThreshold, 0.1f, Time.deltaTime);
            switch (locomotionState)
            {
                case LocomotionState.Idle:
                    animator.SetFloat(moveSpeedHash, 0, 0.1f, Time.deltaTime);
                    break;
                default:    //蹲着不区分行走或奔跑
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * crouchSpeed, 0.1f, Time.deltaTime);
                    break;
            }

            Rigidbody rig = GetComponent<Rigidbody>();
            Vector3 vector3 = new Vector3(animator.velocity.x, rig.velocity.y, animator.velocity.z);
            //rig.velocity = animator.velocity;
            rig.velocity = vector3;
        }

        if (attackState == AttackState.Normal)
        {
            //对玩家运动方向的x和z分量求arctan,得到玩家当前的运动方向和正前方向的夹角（弧度，和状态机的转向速度单位一致）
            float rad = Mathf.Atan2(playerMovement.x, playerMovement.z);
            animator.SetFloat(turnSpeedHash, rad, 0.1f, Time.deltaTime);
            playerTransform.Rotate(0, rad * 180 * Time.deltaTime, 0f);  //转向速度太慢，人为添加一个转向
        }
        
    }
}
