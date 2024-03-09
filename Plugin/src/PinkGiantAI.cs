﻿using System;
using System.Collections;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using System.Linq;
using UnityEngine.PlayerLoop;

namespace GiantSpecimens {

    // You may be wondering, how does the Example Enemy know it is from class PinkGiantAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.
    class PinkGiantAI : EnemyAI {

        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
        #pragma warning disable 0649
        // public Transform turnCompass
        public Collider AttackArea;
        public IEnumerable allAlivePlayers;
        [SerializeField] Collider CollisionShockwaveR;
        [SerializeField] Collider CollisionShockwaveL;
        [SerializeField] Collider CollisionFootR;
        [SerializeField] Collider CollisionFootL;
        #pragma warning restore 0649
        float timer = 0;
        bool eatingEnemy = false;
        bool creatureVoiceHasPlayed = false;
        EnemyAI targetEnemy;
        bool idleGiant = true;
        bool syncAudio = false;
        AudioClip actualSoundToPlay;
        [SerializeField]AudioClip[] stompSounds;
        [SerializeField]GameObject rightBone;
        [SerializeField]GameObject leftBone;
        [SerializeField]GameObject eatingArea;
        Vector3 midpoint;
        enum State {
            IdleAnimation, // Idling
            SearchingForForestKeeper, // Wandering
            RunningToForestKeeper, // Chasing
            EatingForestKeeper, // Eating
        }

        void LogIfDebugBuild(string text) {
            #if DEBUG
            Plugin.Logger.LogInfo(text);
            #endif
        }

        public override void Start()
        {
            base.Start();
            LogIfDebugBuild("Pink Giant Enemy Spawned");
            // creatureAnimator.SetTrigger("startWalk");

            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            currentBehaviourStateIndex = (int)State.IdleAnimation;
            // We make the enemy start searching. This will make it start wandering around.
            stompSounds = this.enemyType.audioClips;
            LogIfDebugBuild(stompSounds[0].name);
            LogIfDebugBuild(this.enemyType.audioClips[0].name);
            rightBone = GameObject.Find("Bone.005.R_end");
            leftBone = GameObject.Find("Bone.005.L_end");
            eatingArea = GameObject.Find("EatingBall");
            StartSearch(transform.position);
        }

        public override void Update(){
            base.Update();

            timer += Time.deltaTime;

            if (currentBehaviourStateIndex == (int)State.EatingForestKeeper && targetEnemy != null) {
                midpoint = (rightBone.transform.position + leftBone.transform.position) / 2;
                // Vector3 direction = eatingArea.transform.position.normalized;
                // Quaternion lookRotation = Quaternion.LookRotation(direction);
                targetEnemy.transform.position = midpoint + new Vector3(0, -1f, 0);
                targetEnemy.transform.LookAt(eatingArea.transform.position);
                targetEnemy.transform.position = midpoint + new Vector3(0, -4f, 0);
                // targetEnemy.transform.rotation = lookRotation;
                //targetEnemy.transform.LookAt(eatingArea.transform);
            }
        }
        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            };

            switch(currentBehaviourStateIndex) {
                case (int)State.IdleAnimation:
                    agent.speed = 0f;
                    if (timer > 14f && idleGiant) {
                        StartCoroutine(PauseDuringIdle());
                    }
                    else if (FindClosestForestKeeperInRange(50f)){
                        DoAnimationClientRpc("startChase");
                        LogIfDebugBuild("Start Target ForestKeeper");
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.RunningToForestKeeper);
                    } // Look for Forest Keeper
                    else {
                        DoAnimationClientRpc("startWalk");
                        LogIfDebugBuild("Start Walking Around");
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForForestKeeper);
                    }
                    
                    break;
                case (int)State.SearchingForForestKeeper:
                    agent.speed = 1.5f;
                    if (!creatureVoiceHasPlayed && syncAudio) {
                        StartCoroutine(PlaySoundSlow(stompSounds));
                        creatureVoiceHasPlayed = true;
                    }
                    else {
                        StartCoroutine(MakeSureAudioSyncd());
                    }
                    if (FindClosestForestKeeperInRange(50f)){
                        DoAnimationClientRpc("startChase");
                        LogIfDebugBuild("Start Target ForestKeeper");
                        StopSearch(currentSearch);
                        syncAudio = false;
                        StopCoroutine(PlaySoundSlow(stompSounds));
                        SwitchToBehaviourClientRpc((int)State.RunningToForestKeeper);
                    } // Look for Forest Keeper
                    break;
                case (int)State.RunningToForestKeeper:
                    agent.speed = 6f;
                    if (!creatureVoiceHasPlayed && syncAudio) {
                        StartCoroutine(PlaySoundFast(stompSounds));
                        creatureVoiceHasPlayed = true;
                    }
                    else {
                        StartCoroutine(MakeSureAudioSyncd());
                    }
                    // Keep targetting closest ForestKeeper, unless they are over 20 units away and we can't see them.
                    if (Vector3.Distance(transform.position, targetEnemy.transform.position) > 100f && !HasLineOfSightToPosition(targetEnemy.transform.position)){
                        LogIfDebugBuild("Stop Target ForestKeeper");
                        DoAnimationClientRpc("startWalk");
                        StartSearch(transform.position);
                        syncAudio = false;
                        StopCoroutine(PlaySoundFast(stompSounds));
                        SwitchToBehaviourClientRpc((int)State.SearchingForForestKeeper);
                        return;
                    }
                    SetDestinationToPosition(targetEnemy.transform.position, checkForPath: false);
                    break;

                case (int)State.EatingForestKeeper:
                    agent.speed = 0f;
                    // Does nothing so far.
                    break;
                    
                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        } 
        void ShakePlayerCamera() {
            foreach (var player in StartOfRound.Instance.allPlayerScripts.Where(x => x.IsSpawned && x.isPlayerControlled && !x.isPlayerDead)) {
                float distance = Vector3.Distance(transform.position, player.transform.position);
                switch (distance) {
                    case < 10f:
                        HUDManager.Instance.ShakeCamera(ScreenShakeType.Long);
                        HUDManager.Instance.ShakeCamera(ScreenShakeType.VeryStrong);
                        HUDManager.Instance.ShakeCamera(ScreenShakeType.VeryStrong);

                        HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
                        HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
                        break;
                    case < 20 and >= 10:
                        HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
                        HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
                        HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
                        break;
                    case < 50f and >= 20:
                        HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
                        HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
                        break;
                }
            }
        }
        bool FindClosestForestKeeperInRange(float range) {
            EnemyAI closestEnemy = null;
            float minDistance = float.MaxValue;

            foreach (var enemy in RoundManager.Instance.SpawnedEnemies) {
                if (enemy.enemyType.enemyName == "ForestGiant") {
                    float distance = Vector3.Distance(transform.position, enemy.transform.position);
                    if (distance < range && distance < minDistance) {
                        minDistance = distance;
                        closestEnemy = enemy;
                    }
                }
            }
            if (closestEnemy != null) {
                targetEnemy = closestEnemy;
                return true;
            }
            return false;
        }

        IEnumerator PauseDuringIdle() {
            yield return new WaitForSeconds(14);
            idleGiant = false;
            StopCoroutine(PauseDuringIdle());
        }
        IEnumerator MakeSureAudioSyncd() {
            yield return new WaitForSeconds(1.8f);
            syncAudio = true;
            StopCoroutine(MakeSureAudioSyncd());
        }
        IEnumerator PlaySoundSlow(AudioClip[] soundToPlay) {
            actualSoundToPlay = soundToPlay[UnityEngine.Random.Range(0, soundToPlay.Length)];
            creatureVoice.PlayOneShot(soundToPlay[UnityEngine.Random.Range(0, soundToPlay.Length)]);
            yield return new WaitForSeconds((float)actualSoundToPlay.length + 0.27f);
            LogIfDebugBuild(actualSoundToPlay.name);
            LogIfDebugBuild((actualSoundToPlay.length+0.27f).ToString());
            creatureVoiceHasPlayed = false;
            ShakePlayerCamera();
        }
        IEnumerator PlaySoundFast(AudioClip[] soundToPlay) {
            actualSoundToPlay = soundToPlay[UnityEngine.Random.Range(0, soundToPlay.Length)];
            creatureVoice.PlayOneShot(soundToPlay[UnityEngine.Random.Range(0, soundToPlay.Length)]);
            yield return new WaitForSeconds(((float)actualSoundToPlay.length+0.27f)/4f);
            LogIfDebugBuild(actualSoundToPlay.name);
            LogIfDebugBuild(((actualSoundToPlay.length+0.27f)/4).ToString());
            creatureVoiceHasPlayed = false;
            ShakePlayerCamera();
        }
        IEnumerator StunGiantRepeatedly(int stunNumber) {
            for (int i = 0; i < stunNumber; i++) {
             targetEnemy.SetEnemyStunned(true, 1.9f);
             yield return new WaitForSeconds(0.25f);   
            }
        }
        IEnumerator EatForestKeeper() {
            DoAnimationClientRpc("eatForestKeeper");
            targetEnemy.CancelSpecialAnimationWithPlayer();
            StartCoroutine(StunGiantRepeatedly(60));
            LogIfDebugBuild($"{targetEnemy}");
            yield return new WaitForSeconds(10);
            targetEnemy.KillEnemyOnOwnerClient(overrideDestroy: true);
            yield return new WaitForSeconds(5);
            StopCoroutine(EatForestKeeper());
            StopCoroutine(StunGiantRepeatedly(60));
            eatingEnemy = false;
            syncAudio = false;
            idleGiant = true;
            SwitchToBehaviourClientRpc((int)State.IdleAnimation);
        }
        
        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy) 
        {
            if (collidedEnemy == targetEnemy && eatingEnemy == false) {
                LogIfDebugBuild("Pink Giant hitting this guy:" + targetEnemy);
                SwitchToBehaviourClientRpc((int)State.EatingForestKeeper);
                eatingEnemy = true;
                if (eatingEnemy) {
                    StartCoroutine(EatForestKeeper());
                }
            }
        }
        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }
    }
}