//===================================================
// FileName:      ThridPersonController.cs         
// Author:        Allent Lee	
// CreateTime:    2023-05-07 00:48:21	
// E-mail:        xiaomo_lzm@163.com
// Description:   
//===================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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

public class ThirdPersonCtrl : MonoBehaviour
{
    private Transform playerTransform;
    private Transform cameraTransform;
    private Animator animator;
    private CapsuleCollider capsuleCollider;
    
    // private AnimatorStateInfo animState;    //动画状态
    public Transform rightHandObject;           //刀柄IK位置

    [HideInInspector]
    public PlayerPosture playerPosture = PlayerPosture.Stand;
    private const float crouchThreshold = 0f;   //和状态机里空手运动BlendTree的阈值一致
    private const float standThreshold = 1f;
    private const float midairThreshold = 2.1f; //多0.1保证跳跃时参数大于2，防止抖动

    [HideInInspector]
    public LocomotionState locomotionState = LocomotionState.Idle;
    [HideInInspector]
    public AttackState attackState = AttackState.Attack;
    private const float crouchSpeed = 1.5f;   //蹲着走速度
    private const float walkSpeed = 2.5f;
    private const float runSpeed = 5.5f;

    private Vector2 MoveInput;
    private bool isRunning;
    private bool isCrouch;
    private bool isJumping;
    private bool isAttacking;

    private int postureHash;    //动画状态机玩家姿态参数哈希值，使用哈希值比直接使用字符串效率高
    private int moveSpeedHash;
    private int turnSpeedHash;
    private int verticalVelHash;
    
    Vector3 playerMovement = Vector3.zero;  //玩家实际要移动的方向
    private bool isGrounded = false;
    private float groundCheckOffset = 0.2f; //地面检测射线偏移量
    public float gravity = -15f;    //模拟重力，很多游戏重力都超过实际
    private float verticalVelocity; //当前角色垂直方向速度
    public float maxHeight = 1.2f;   //最大跳跃高度
    private float fallMultiplier = 1.5f;    //下落速度相对跳跃速度的倍率，下落速度快虽然反物理，但能增强游戏质感
    
    private static readonly int CACHE_SIZE = 3; //离地前几帧速度缓存帧数
    private Vector3[] velCache = new Vector3[CACHE_SIZE];
    private int currentCacheIndex = 0;  //缓存词中最老的向量索引
    Vector3 averageVel = Vector3.zero;
    
    // Start is called before the first frame update
    void Start()
    {
        playerTransform = this.transform;   //提升访问效率，避免每次都去访问组件
        cameraTransform = Camera.main.transform;
        animator = GetComponent<Animator>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        // animState = animator.GetCurrentAnimatorStateInfo(animator.GetLayerIndex("Base Layer"));
        postureHash = Animator.StringToHash("玩家姿态");
        moveSpeedHash = Animator.StringToHash("移动速度");
        turnSpeedHash = Animator.StringToHash("转弯速度");
        verticalVelHash = Animator.StringToHash("垂直速度");
    }

    // Update is called once per frame
    void Update()
    {
        CheckGrounded();
        SwitchPlayerStates();
        CaculateGravity();
        Jump();
        CaculateInputDirection();
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
    public void GetAttackInput(InputAction.CallbackContext ctx)
    {
        isAttacking = ctx.ReadValueAsButton();
    }

    public void GetJumpInput(InputAction.CallbackContext ctx)
    {
        isJumping = ctx.ReadValueAsButton();
        //Debug.Log("is jump!");
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
        // if (isJumping)
        // {
        //     playerPosture = PlayerPosture.Midair;
        // }
        if (!isGrounded)    //不在地面就要处于滞空，包括从平台下落
        {
            playerPosture = PlayerPosture.Midair;
        }
        else if (isCrouch)
        {
            playerPosture = PlayerPosture.Crouch;
        }
        else
        {
            playerPosture = PlayerPosture.Stand;
        }

        if (MoveInput.magnitude == 0)   //玩家没输入
        {
            locomotionState = LocomotionState.Idle;
            averageVel = Vector3.zero;  //离地前几帧平均值设为0，防止原地跳取之前的值
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
        // Vector3 camForwawrdProjection = new Vector3(MoveInput.x, 0, MoveInput.y);
        // float y = cameraTransform.rotation.eulerAngles.y;
        // playerMovement = Quaternion.Euler(0, y, 0) * camForwawrdProjection;
        Vector3 camForwawrdProjection = new Vector3(cameraTransform.forward.x, 0, cameraTransform.forward.z).normalized;//相机在水平方向的投影
        playerMovement = MoveInput.y * camForwawrdProjection + MoveInput.x * cameraTransform.right;

        playerMovement = playerTransform.InverseTransformVector(playerMovement);   //将世界坐标转为本地坐标
    }
    
    //检测是否在地上
    private void CheckGrounded()
    {
        Vector3 pointBottom, pointTop;  //分别为胶囊体起点球心，胶囊体终点球心
        pointBottom = playerTransform.position + playerTransform.up * capsuleCollider.radius - playerTransform.up * 0.1f;
        pointTop = playerTransform.position + playerTransform.up * capsuleCollider.height - playerTransform.up * capsuleCollider.radius;
        LayerMask ignoreMask = ~(1 << LayerMask.NameToLayer("Player")); //LayerMask.GetMask("Player")
        Debug.DrawLine(pointBottom, pointTop,Color.green);
 
        // RaycastHit hitInfo;
        // if (Physics.SphereCast(playerTransform.position, capsuleCollider.radius, Vector3.down, out hitInfo,
        //         ((capsuleCollider.height / 2f) - capsuleCollider.radius) + advancedSettings.groundCheckDistance))
        
        // Collider[] colliders = Physics.OverlapCapsule(pointBottom, pointTop, capsuleCollider.radius, ignoreMask);
        // if (colliders.Length != 0)
        if(Physics.CheckCapsule(pointBottom, pointTop, capsuleCollider.radius, ignoreMask))
        {
            Debug.LogFormat("is ground");
            isGrounded = true;
        }
        else
        {
            Debug.Log("not int ground");
            isGrounded = false;
        }

        return;
    }

    //计算重力影响
    void CaculateGravity()
    {
        if (isGrounded)
        {
            verticalVelocity = gravity * Time.deltaTime;   //角色站在地面上，垂直速度归零，如果使用characterController.isGrounded,这里要一直施加向下的力，使用值gravity * Time.deltaTime
            return;
        }
        else
        {
            if (verticalVelocity <= 0)  //下落阶段
            {
                verticalVelocity += gravity * fallMultiplier * Time.deltaTime;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;   //重力加速度
            }
        }
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
                    Debug.LogFormat("set walk speed is {0}",playerMovement.magnitude * walkSpeed);
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
        }
        else if (playerPosture == PlayerPosture.Midair)
        {
            animator.SetFloat(postureHash, midairThreshold);
            animator.SetFloat(verticalVelHash, verticalVelocity);
            Debug.LogFormat("set Midair status");
        }

        if (attackState == AttackState.Normal)
        {
            //对玩家运动方向的x和z分量求arctan,得到玩家当前的运动方向和正前方向的夹角（弧度，和状态机的转向速度单位一致）
            float rad = Mathf.Atan2(playerMovement.x, playerMovement.z);
            animator.SetFloat(turnSpeedHash, rad, 0.1f, Time.deltaTime);
            playerTransform.Rotate(0, rad * 180 * Time.deltaTime, 0f);  //转向速度太慢，人为添加一个转向
        }
    }
    
    //计算离地前缓存帧的平均速度，游戏中平滑、去噪也是基于平均值
    Vector3 AverageVel(Vector3 newVel)
    {
        velCache[currentCacheIndex] = newVel;   //替换缓存帧最老的向量
        currentCacheIndex++;
        currentCacheIndex %= velCache.Length;
        Vector3 avarage = Vector3.zero;
        for (int i = 0; i < velCache.Length; i++)
        {
            avarage += velCache[i];
        }

        return avarage / velCache.Length;
    }

    //接管rootmotion位移
    private void OnAnimatorMove()
    {
        if (playerPosture != PlayerPosture.Midair)
        {
            Vector3 playerDeltaMovement = animator.deltaPosition;
            playerDeltaMovement.y = verticalVelocity * Time.deltaTime;
            //Debug.LogFormat("playerDeltaMovement.y:{0}",playerDeltaMovement.y);
            playerTransform.position += playerDeltaMovement;
            averageVel = AverageVel(animator.velocity);
            //Debug.LogFormat("avgvel:{0}",averageVel);
        }
        else
        {
            averageVel.y = verticalVelocity;
            //沿用地面速度，比如角色离地前几帧的平均速度，解决跳跃时水平方向移动异常问题
            Vector3 playerDeltaMovement = averageVel * Time.deltaTime;  //平均速度*时间，animator.deltaPosition会受帧率影响，跳跃前后帧率差异可能较大，平均值不具备参考性
            //Debug.LogFormat("playerDeltaMovement.y:{0}",playerDeltaMovement.y);
            playerTransform.position += playerDeltaMovement;
        }
    }

    private void Jump()
    {
        if (isGrounded && isJumping)
        {
            Debug.Log("jump here");
            verticalVelocity = Mathf.Sqrt(-2 * gravity * maxHeight);        //自由落体公式v^2 = 2gh => v = sqrt(2gh)
        }
    }
}
