/* TODO

- lerp slide
- lerp scale
- lerp rotate
- lerp color
- optimize wiggle
- fix generation
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
      public bool isAni;
      public SlidingCube(KMSelectable kms, int ind, int goal){
         KMS = kms;
         CurrentPosInd = ind;
         GoalPosInd = goal;
         isAni = false;
      }

      public IEnumerator Shrink(){
         float scale = KMS.transform.localScale.x * 0.99f;
         int failsafe = 0;
         while(scale > 0.05f && failsafe < 10){
            KMS.transform.localScale = Vector3.one*scale;
            scale *= scale;
            failsafe++;
            yield return new WaitForSeconds(0.01f);
         }
         KMS.transform.localScale = Vector3.zero;
      }

      public IEnumerator Enlargen(){
         float scale = KMS.transform.localScale.x + 0.01f;
         int failsafe = 0;
         while(scale < 0.99f && failsafe < 10){
            KMS.transform.localScale = Vector3.one*scale;
            scale = (float)Math.Sqrt(scale);
            failsafe++;
            yield return new WaitForSeconds(0.01f);
         }
         KMS.transform.localScale = Vector3.one;
      }

      public IEnumerator Slide(float speed = 0.5f, int maxWait = 10){
         Vector3 fro = KMS.transform.localPosition;
         Vector3 to = IntToPos(CurrentPosInd);

         //jank code but it works :P
         int failsafe = 0;
         while(fro != to && failsafe < maxWait){
            to = IntToPos(CurrentPosInd);
            fro = KMS.transform.localPosition;
            KMS.transform.localPosition += (to - fro)*speed;
            failsafe++;
            yield return new WaitForSeconds(0.01f);
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
         if(wait){
            yield return new WaitForSeconds(1f);
         }

         while(isAni) yield return new WaitForSeconds(0.01f);
         isAni = true;

         int i = 0;
         Color clr = KMS.GetComponent<Renderer>().material.color;
         Color tclr = StaticCubeMats[order[i]].color;
         float r = 0, g = 0, b = 0;
         float spd = 0.07f;
         int failsafe = 0;
         if(!wait) spd = 0.2f;
         while(i < order.Length){
            r = (tclr.r - clr.r)*spd + clr.r;
            g = (tclr.g - clr.g)*spd + clr.g;
            b = (tclr.b - clr.b)*spd + clr.b;

            clr = new Color(r,g,b);
            KMS.GetComponent<Renderer>().material.color = clr;

            if((failsafe > 20 && !wait) || clr == tclr){
               i++;
               if(i >= order.Length) break;
               tclr = StaticCubeMats[order[i]].color;
               failsafe = 0;
            }
            
            failsafe++;
            yield return new WaitForSeconds(0.002f);
         }
         isAni = false;
      }
   }

   private SlidingCube[] CubeArr = new SlidingCube[64];
   private int HoleCubeIndex;

   void Awake () {
      ModuleId = ModuleIdCounter++;
      GetComponent<KMBombModule>().OnActivate += Activate;
      FirstActivation = true; //setup in Activate()

      Debug.LogFormat("[6D Sliding Puzzle #{0}] Running v1.0.2 | Startup audio: {1}", ModuleId, FirstActivation);

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
      Shuffle(Scramble);

      for(int i = 0; i < 64; i++){
         CubeArr[i] = new SlidingCube(CubeSelectables[i], Scramble[i], i);
         CubeArr[i].KMS.transform.localPosition = new Vector3(0, -5, 0);
         CubeArr[i].KMS.GetComponent<Renderer>().material = CubeMats[3];
         StartCoroutine(CubeArr[i].Wiggle());
      }

      HoleCubeIndex = 0;
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
               StartCoroutine(CubeArr[j].AniMat(new int[] {0,2}, false));
            }
            if(CubeArr[j].GoalPosInd == CubeArr[i].CurrentPosInd){
               StartCoroutine(CubeArr[j].AniMat(new int[] {3,2}, false));
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

      CheckCubeGoal(CubeArr[i]);
      CheckCubeGoal(CubeArr[HoleCubeIndex]);

      int correctCubes = 0;

      for(int j = 0; j < 64; j++){
         if(CubeArr[j].CurrentPosInd == CubeArr[j].GoalPosInd){
            correctCubes++;
         }
      }

      if(correctCubes >= 62){
         Solve();
      }

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
         Debug.LogFormat("[6D Sliding Puzzle #{0}] Audio: {0}", ModuleId);
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

   static Vector3 IntToPos (int i){
      if(i == -1) return new Vector3(0f, -5f, 0f);
      Vector3 pos = new Vector3(-2.5f, -2.5f, -2.5f);
      int[] arr = IntToArr(i);
      //XYZ RST
      if(arr[0] == 1) pos.x += 1.2f;
      if(arr[1] == 1) pos.y += 1.2f;
      if(arr[2] == 1) pos.z += 1.2f;
      if(arr[3] == 1) pos.x += 3.8f;
      if(arr[4] == 1) pos.z += 3.8f;
      if(arr[5] == 1) pos.y += 3.8f;
      return pos;
   }

   void Shuffle(int[] texts){
      //normally i would say "trust me on this" but no, dont
      //idek if this even works
      bool[] targets = new bool[texts.Length];
      int targetsCount = 0;
      bool containsHole = false;
      int temp;

      //itterate 64 times      
      for(int i = 0; i < texts.Length; i++){
         
         //setup target cubes to be cycled
         for (int t = 0; t < texts.Length; t++ ){
            if(Rnd.Range(0, 6) == 0){
               targets[t] = true;
               targetsCount++;
               if(texts[t] == 0) containsHole = true;
            } else {
               targets[t] = false;
            }
         }

         //odd if no hole, even if ya hole; swap parity
         if(targetsCount % 2 == 0 && !containsHole){
            temp = Rnd.Range(0, texts.Length);
            targets[temp] = !targets[temp];
         } else if(targetsCount % 2 == 1 && containsHole){
            temp = Rnd.Range(0, texts.Length);
            targets[temp] = !targets[temp];
         }

         //cycle
         int laststored = -1;
         int firstPos;
         for(int t = 0; t < texts.Length; t++){
            if(targets[t]){
               if(laststored == -1) firstPos = t;
               temp = texts[t];
               texts[t] = laststored;
               laststored = temp;
            }
         }

         //last swap
         for(int t = 0; t < texts.Length; t++){
            if(texts[t] == -1){
               texts[t] = laststored;
            }
         }
      }
   }

   void CheckCubeGoal(SlidingCube a){
      if(a.CurrentPosInd == a.GoalPosInd){
         StartCoroutine(a.AniMat(new int[] {1}, false));
      } else {
         StartCoroutine(a.AniMat(new int[] {2}, false));
      }
   }

   //anis
   IEnumerator StartupAni(){
      //cymk
      for(int i = 0; i < 64; i++){
         StartCoroutine(CubeArr[i].Slide(0.04f, 180));
         if(CubeArr[i].CurrentPosInd == CubeArr[i].GoalPosInd){
            StartCoroutine(CubeArr[i].AniMat(new int[] {3,0,1}, true));
         } else {
            StartCoroutine(CubeArr[i].AniMat(new int[] {3,0,2}, true));
         }
         yield return new WaitForSeconds(0.02f);
      }
      yield return new WaitForSeconds(5.72f);
      ModState = "READY";
   }

   IEnumerator RotatePuzzle(int axis, int dir){
      ModState = "TURNING";
      //axis 012 xyz
      //dir 1 cw; -1 ccw

      if(dir != 0){
         int rotaCount = 0;
         while(rotaCount < 15){
            rotaCount++;

            switch(axis){
               case 0:
                  CubeParent.transform.localRotation = Quaternion.Euler(dir*6.0f, 0.0f, 0.0f) * CubeParent.transform.localRotation;
                  break;
               case 1:
                  CubeParent.transform.localRotation = Quaternion.Euler(0.0f, dir*6.0f, 0.0f) * CubeParent.transform.localRotation;
                  break;
               case 2:
                  CubeParent.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, dir*6.0f) * CubeParent.transform.localRotation;
                  break;
            }

            yield return new WaitForSeconds(0.01f);
         }

      } else {
         //swap halves mechanic that will not be in release
      }

      ModState = "READY";
   }

   IEnumerator WinAni(){
      ModState = "SOLVED";
      //cymk
      StartCoroutine(RotateBetter());
      for(int i = 0; i < 64; i++) StartCoroutine(CubeArr[i].AniMat(new int[] {0}, true));
      yield return new WaitForSeconds(0.7f);
      for(int i = 0; i < 64; i++){
         StartCoroutine(CubeArr[i].AniMat(new int[] {2}, true));
         yield return new WaitForSeconds(0.02f);
      }
      yield return new WaitForSeconds(1.4f);

      for(int i = 0; i < 64; i++){
         CubeArr[i].CurrentPosInd = -1;
         StartCoroutine(CubeArr[i].Slide(0.3f, 180));
         yield return new WaitForSeconds(0.01f);
      }
      CubeArr[0].KMS.AddInteractionPunch(4f);
   }

   IEnumerator RotateBetter(){
      Vector3 rot = CubeParent.transform.localRotation.eulerAngles;
      Vector3 rot2 = Vector3.zero;
      for(int i = 0; i < 361; i++){
         rot2.x = Mathf.LerpAngle(rot.x, 0, Mathf.Sqrt(i)/19f);
         rot2.y = Mathf.LerpAngle(rot.y, 0, Mathf.Sqrt(i)/19f);
         rot2.z = Mathf.LerpAngle(rot.z, 0, Mathf.Sqrt(i)/19f);
         CubeParent.transform.localRotation = Quaternion.Euler(rot2.x, rot2.y, rot2.z);
         yield return new WaitForSeconds(0.015f);
      }
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