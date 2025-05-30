/* TODO
- lerp color
- optimize wiggle
- tp handler + auto
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using KModkit;
using Rnd = UnityEngine.Random;
//using Math = ExMath;

public class sixDSlidingPuzzleScript : MonoBehaviour {
   public KMBombInfo Bomb;
   public KMAudio Audio;

   private static int ModuleIdCounter = 1;
   private int ModuleId;
   private bool ModuleSolved;
   private static bool FirstActivation = true;

   public KMSelectable[] CubeSelectables;
   public KMSelectable[] HingeSelectables;
   public GameObject CubeParent;
   public Material[] CubeMats;
   public static Material[] StaticCubeMats;
   public GameObject StatusLight;

   private string ModState;
   private int HingePrevious;

   public struct SlidingCube{
      public KMSelectable KMS;
      public int CurrentPosInd;
      public int GoalPosInd;
      public Coroutine AniMatCoroutine;
      
      public SlidingCube(KMSelectable kms, int ind, int goal){
         KMS = kms;
         CurrentPosInd = ind;
         GoalPosInd = goal;
         AniMatCoroutine = null;
      }

      public IEnumerator Shrink(){
         Vector3 fro = KMS.transform.localScale;
         Vector3 to = Vector3.zero;
         Vector3 newV3;

         for(int i = 0; i < 10; i++){
            newV3.x = Mathf.LerpAngle(fro.x, to.x, sigmoidLerp(i/10f));
            newV3.y = Mathf.LerpAngle(fro.y, to.y, sigmoidLerp(i/10f));
            newV3.z = Mathf.LerpAngle(fro.z, to.z, sigmoidLerp(i/10f));
            KMS.transform.localScale = newV3;
            yield return null;
         }
         KMS.transform.localScale = to;
      }

      public IEnumerator Enlargen(){
         Vector3 fro = KMS.transform.localScale;
         Vector3 to = Vector3.one;
         Vector3 newV3;

         for(int i = 0; i < 10; i++){
            newV3.x = Mathf.LerpAngle(fro.x, to.x, sigmoidLerp(i/10f));
            newV3.y = Mathf.LerpAngle(fro.y, to.y, sigmoidLerp(i/10f));
            newV3.z = Mathf.LerpAngle(fro.z, to.z, sigmoidLerp(i/10f));
            KMS.transform.localScale = newV3;
            yield return null;
         }
         KMS.transform.localScale = to;
      }

      public IEnumerator Slide(float speed = 10f){
         Vector3 fro = KMS.transform.localPosition;
         Vector3 to = IntToPos(CurrentPosInd);
         Vector3 newV3;

         for(int i = 0; i < speed; i++){
            newV3.x = Mathf.LerpAngle(fro.x, to.x, sigmoidLerp(i/speed));
            newV3.y = Mathf.LerpAngle(fro.y, to.y, sigmoidLerp(i/speed));
            newV3.z = Mathf.LerpAngle(fro.z, to.z, sigmoidLerp(i/speed));
            KMS.transform.localPosition = newV3;
            yield return null;
         }
         KMS.transform.localPosition = to;
      }

      public IEnumerator Wiggle(){
         //shoutouts to GhostSalt for putting this in GoL3D, you're epic
         float speed1 = 2f;
         float speed2 = 1f;
         float speed3 = Mathf.PI * 2f / 3f;
         float maxAngle = 3f;
         float variance = 1f;

         speed1 += Rnd.Range(-variance, variance);
         speed2 += Rnd.Range(-variance / 2, variance / 2);
         speed3 += Rnd.Range(-variance, variance);
         while (true){
            KMS.transform.localEulerAngles = new Vector3(Mathf.Sin((speed1 / 4) * Time.time) * maxAngle, Mathf.Sin((speed2 / 4) * Time.time) * maxAngle, Mathf.Sin((speed3 / 4) * Time.time) * maxAngle);
            yield return null;
         }
      }

      public IEnumerator AniMat(int[] order, bool wait = false){
         //0123 => cymk
         Color fro = KMS.GetComponent<Renderer>().material.color;
         Color to = StaticCubeMats[order[0]].color;
         Color newClr = fro;

         int speed = wait ? 140 : 30;
         if(wait) yield return new WaitForSeconds(1f);

         for(int j = 0; j < order.Length; j++){
            for(int i = 0; i < speed; i++){
               newClr.r = Mathf.Lerp(fro.r, to.r, sigmoidLerp(i/(float)speed));
               newClr.g = Mathf.Lerp(fro.g, to.g, sigmoidLerp(i/(float)speed));
               newClr.b = Mathf.Lerp(fro.b, to.b, sigmoidLerp(i/(float)speed));
               KMS.GetComponent<Renderer>().material.color = newClr;
               yield return null;
            }
            KMS.GetComponent<Renderer>().material.color = to;
            if (j < order.Length - 1){
               fro = to;
               to = StaticCubeMats[order[j + 1]].color;
            }
         }
      }
   }

   private SlidingCube[] CubeArr = new SlidingCube[64];
   private int HoleCubeIndex;

   void Awake () {
      ModuleId = ModuleIdCounter++;
      GetComponent<KMBombModule>().OnActivate += Activate;
      FirstActivation = true; //setup in Activate()

      Debug.LogFormat("[6D Sliding Puzzle #{0}] Running v1.0.5", ModuleId);

      ModState = "START";
      StaticCubeMats = CubeMats;

      foreach (KMSelectable Qb in CubeSelectables) {
         Qb.OnInteract += delegate () { CubePress(Qb); return false; };
      }

      foreach (KMSelectable Hinge in HingeSelectables) {
         Hinge.OnInteract += delegate () { HingePress(Hinge); return false; };
      }
      HingePrevious = -1;

      int[] Scramble =  new int[64];
      for(int i = 0; i < 64; i++) Scramble[i] = i;

      HoleCubeIndex = Rnd.Range(0, 64);
      Shuffle(Scramble);
      for(int i = 0; i < 64; i++){
         CubeArr[i] = new SlidingCube(CubeSelectables[i], Scramble[i], i);
         CubeArr[i].KMS.transform.localPosition = new Vector3(0, -5, 0);
         CubeArr[i].KMS.GetComponent<Renderer>().material = CubeMats[3];
         StartCoroutine(CubeArr[i].Wiggle());
      }
      StartCoroutine(CubeArr[HoleCubeIndex].Shrink());
   }

   void CubePress(KMSelectable Qb){
      if(ModuleSolved || ModState != "READY") return;
      int i = 0;
      for(; i < 64; i++){
         if(CubeArr[i].KMS == Qb)
            break;
      }

      int adjint = IsAdjacent(CubeArr[HoleCubeIndex], CubeArr[i]);
      
      if(adjint == -1){
         if(CubeArr[i].CurrentPosInd == CubeArr[i].GoalPosInd) return;
         for(int j = 0; j < 64; j++){
            if(CubeArr[i].GoalPosInd == CubeArr[j].CurrentPosInd){
               if(CubeArr[j].AniMatCoroutine != null) StopCoroutine(CubeArr[j].AniMatCoroutine);
               CubeArr[j].AniMatCoroutine = StartCoroutine(CubeArr[j].AniMat(new int[] {0,2}, false));
            }
            if(CubeArr[j].GoalPosInd == CubeArr[i].CurrentPosInd){
               if(CubeArr[j].AniMatCoroutine != null) StopCoroutine(CubeArr[j].AniMatCoroutine);
               CubeArr[j].AniMatCoroutine = StartCoroutine(CubeArr[j].AniMat(new int[] {3,2}, false));
            }
         }
         return;
      }

      //use to swap shit
      int b;

      if(adjint <= 2){ //slide
         b = CubeArr[HoleCubeIndex].CurrentPosInd;
         CubeArr[HoleCubeIndex].CurrentPosInd = CubeArr[i].CurrentPosInd;
         CubeArr[i].CurrentPosInd = b;
         StartCoroutine(CubeArr[i].Slide());
         StartCoroutine(CubeArr[HoleCubeIndex].Slide());

      } else { //scale
         b = CubeArr[HoleCubeIndex].GoalPosInd;
         CubeArr[HoleCubeIndex].GoalPosInd = CubeArr[i].GoalPosInd;
         CubeArr[i].GoalPosInd = b;

         StartCoroutine(CubeArr[HoleCubeIndex].Enlargen());
         StartCoroutine(CubeArr[i].Shrink());
         
         b = HoleCubeIndex;
         HoleCubeIndex = i;
         i = b;
      }

      CheckCubeGoal(i);
      CheckCubeGoal(HoleCubeIndex);

      for(int j = 0; j < 64; j++){
         if(CubeArr[j].CurrentPosInd != CubeArr[j].GoalPosInd){
            return;
         }
      }
      Solve();
   }

   void HingePress(KMSelectable Hinge){
      if(ModuleSolved || ModState != "READY") return;

      int HingeCurrent = 0;
      for(; HingeCurrent < 8; HingeCurrent++){
         if(Hinge == HingeSelectables[HingeCurrent]){
            break;
         }
      }
      Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, HingeSelectables[HingeCurrent].transform);

      if(HingePrevious == -1){
         HingePrevious = HingeCurrent;
         return;
      }

      int delta = (HingeCurrent - HingePrevious + 8) % 8;

      if(HingePrevious % 2 == 0 && delta == 1){
         //cw
         StartCoroutine(RotatePuzzle(1, 1));
      } else if(HingePrevious %2 == 1 && delta == 7){
         //ccw
         StartCoroutine(RotatePuzzle(1, -1));
      } else if ( (HingePrevious == 0 && HingeCurrent == 3) || (HingePrevious == 7 && HingeCurrent == 4)){
         //tip down
         StartCoroutine(RotatePuzzle(0, -1));
      }  else if ( (HingePrevious == 3 && HingeCurrent == 0) || (HingePrevious == 4 && HingeCurrent == 7)){
         //tip up
         StartCoroutine(RotatePuzzle(0, 1));
      }  else if ( (HingePrevious == 1 && HingeCurrent == 6) || (HingePrevious == 2 && HingeCurrent == 5)){
         //tip left
         StartCoroutine(RotatePuzzle(2, 1));
      }  else if ( (HingePrevious == 6 && HingeCurrent == 1) || (HingePrevious == 5 && HingeCurrent == 2)){
         //tip right
         StartCoroutine(RotatePuzzle(2, -1));
      }
      HingePrevious = -1;
   }

   void Activate () {
      //gurdian battle | botw
      if(FirstActivation){
         FirstActivation = false;
         Audio.PlaySoundAtTransform("guardian", transform);
      }
      StartCoroutine(StartupAni());
   }

   void Solve () {
      GetComponent<KMBombModule>().HandlePass();
      StartCoroutine(WinAni());
      Audio.PlaySoundAtTransform("guardianWin", transform);
      ModuleSolved = true;
   }

   //position fuckery
   static int[] IntToArr (int i) {
      string posString = Convert.ToString(i, 2);
      while(posString.Length < 6){
         posString = "0" + posString;
      }
      int[] arr = {0,0,0,0,0,0};
      for(int j = 0; j<6; j++){
         arr[j] = (posString[5-j] == '1') ? 1 : 0;
      }
      return arr;
   }

   static int ArrToInt (int[] arr) {
      string posString = "";
      for(int j = 5; j>=0; j--){
         posString += arr[j].ToString();
      }
      return Convert.ToInt32(posString, 2);
   }

   static int IsAdjacent (SlidingCube a, SlidingCube b){
      int[] x = IntToArr(a.CurrentPosInd);
      int[] y = IntToArr(b.CurrentPosInd);

      int axisOfChange = 0;
      int checkma = 0;
      for(int i = 0; i < 6; i++){
         if(x[i] != y[i]){
            checkma++;
            axisOfChange = i;
         }
      }
      if(checkma == 1){return axisOfChange;}
      else {return -1;}
   }

   static int IsAdjacent (int a, int b){
      int[] x = IntToArr(a);
      int[] y = IntToArr(b);

      int axisOfChange = 0;
      int checkma = 0;
      for(int i = 0; i < 6; i++){
         if(x[i] != y[i]){
            checkma++;
            axisOfChange = i;
         }
      }
      if(checkma == 1){return axisOfChange;}
      else {return -1;}
   }

   static Vector3 IntToPos (int i){
      if(i == -1) return new Vector3(0f, -5f, 0f);
      Vector3 pos = new Vector3(-2.5f, -2.5f, -2.5f);
      int[] arr = IntToArr(i);
      //XYZ RTS
      if(arr[0] == 1) pos.x += 1.2f;
      if(arr[1] == 1) pos.y += 1.2f;
      if(arr[2] == 1) pos.z += 1.2f;
      if(arr[3] == 1) pos.x += 3.8f;
      if(arr[4] == 1) pos.y += 3.8f;
      if(arr[5] == 1) pos.z += 3.8f;
      return pos;
   }

   void Shuffle(int[] texts){
      //it just works i think
      int HolePos = HoleCubeIndex;

      for(int i = 0; i < 512; i++){
         int axisToSwap = Rnd.Range(0, 6);
         int[] TargetArr = IntToArr(HolePos);
         TargetArr[axisToSwap] = (TargetArr[axisToSwap] + 1) % 2;
         int TargetPos = ArrToInt(TargetArr);

         int b = texts[HolePos];
         texts[HolePos] = texts[TargetPos];
         texts[TargetPos] = b;

         b = HolePos;
         HolePos = TargetPos;
         TargetPos = b;
      }
      HoleCubeIndex = HolePos;
   }

   void CheckCubeGoal(int i){
      if(CubeArr[i].AniMatCoroutine != null) StopCoroutine(CubeArr[i].AniMatCoroutine);
      if(CubeArr[i].CurrentPosInd == CubeArr[i].GoalPosInd){
         CubeArr[i].AniMatCoroutine = StartCoroutine(CubeArr[i].AniMat(new int[] {1}, false));
      } else {
         CubeArr[i].AniMatCoroutine = StartCoroutine(CubeArr[i].AniMat(new int[] {2}, false));
      }
   }

   //anis
   IEnumerator StartupAni(){
      //cymk
      for(int i = 0; i < 64; i++){
         StartCoroutine(CubeArr[i].Slide(150f));
         if(CubeArr[i].CurrentPosInd == CubeArr[i].GoalPosInd){
            CubeArr[i].AniMatCoroutine = StartCoroutine(CubeArr[i].AniMat(new int[] {0,1}, true));
         } else {
            CubeArr[i].AniMatCoroutine = StartCoroutine(CubeArr[i].AniMat(new int[] {0,2}, true));
         }
         yield return new WaitForSeconds(0.02f);
      }
      yield return new WaitForSeconds(4f);
      Debug.LogFormat("[6D Sliding Puzzle #{0}] Module is ready", ModuleId);
      ModState = "READY";
   }

   IEnumerator RotatePuzzle(int axis, int dir) {
      //axis 012 xyz
      //dir 1cw -1ccw
      ModState = "ROTATING";
      Quaternion currentRotation = CubeParent.transform.localRotation;
      Vector3 rotationAxis = Vector3.zero;

      switch(axis) {
         case 0:
            rotationAxis = Vector3.right;
            break;
         case 1:
            rotationAxis = Vector3.up;
            break;
         case 2:
            rotationAxis = Vector3.forward;
            break;
      }

      Quaternion targetRotation = Quaternion.AngleAxis(90 * dir, rotationAxis) * currentRotation;
      float rotationDuration = 0.8f;
      float elapsedTime = 0f;

      while (elapsedTime < rotationDuration) {
         CubeParent.transform.localRotation = Quaternion.Slerp(currentRotation, targetRotation, sigmoidLerp(elapsedTime / rotationDuration));
         elapsedTime += Time.deltaTime;
         yield return null;
      }

      CubeParent.transform.localRotation = targetRotation;
      ModState = "READY";
   }

   IEnumerator WinAni(){
      ModState = "SOLVED";
      //cymk
      StartCoroutine(RotateToZero());
      for(int i = 0; i < 64; i++){
         if(CubeArr[i].AniMatCoroutine != null) StopCoroutine(CubeArr[i].AniMatCoroutine);
         CubeArr[i].AniMatCoroutine = StartCoroutine(CubeArr[i].AniMat(new int[] {0}, true));
      }
      yield return new WaitForSeconds(0.7f);
      for(int i = 0; i < 64; i++){
         if(CubeArr[i].AniMatCoroutine != null) StopCoroutine(CubeArr[i].AniMatCoroutine);
         CubeArr[i].AniMatCoroutine = StartCoroutine(CubeArr[i].AniMat(new int[] {2}, true));
         yield return new WaitForSeconds(0.02f);
      }
      yield return new WaitForSeconds(1.4f);

      for(int i = 0; i < 64; i++){
         CubeArr[i].CurrentPosInd = -1;
         StartCoroutine(CubeArr[i].Slide());
         yield return null;
      }
      CubeArr[0].KMS.AddInteractionPunch(4f);
   }

   IEnumerator RotateToZero(){
      Vector3 rot = CubeParent.transform.localRotation.eulerAngles;
      Vector3 rot2 = Vector3.zero;

      //you WILL get cool animations 
      if(rot.x == 0) rot.x = 180;
      if(rot.y == 0) rot.y = 180;
      if(rot.z == 0) rot.z = 180;

      for(int i = 0; i < 350; i++){
         rot2.x = Mathf.LerpAngle(rot.x, 0, sigmoidLerp(i/600f));
         rot2.y = Mathf.LerpAngle(rot.y, 0, sigmoidLerp(i/600f));
         rot2.z = Mathf.LerpAngle(rot.z, 0, sigmoidLerp(i/600f));
         CubeParent.transform.localRotation = Quaternion.Euler(rot2.x, rot2.y, rot2.z);
         yield return new WaitForSeconds(0.015f);
      }
   }

   static float sigmoidLerp(float i){
      //paste this into desmos
      //   \frac{2.013475894}{1+e^{-7x}}-1
      return 2.013475894f/(1+Mathf.Pow(2.718281828459f, -7*i)) - 1.0f;
   }

#pragma warning disable 414
   private readonly string TwitchHelpMessage = @"MOD CURRENTLY UNSUPPORTED: !{0} <anything> TO SOLVE.";
#pragma warning restore 414

   IEnumerator ProcessTwitchCommand (string Command) {
      Solve();
      yield return null;
   }

   IEnumerator TwitchHandleForcedSolve () {
      Solve();
      yield return null;
   }
}