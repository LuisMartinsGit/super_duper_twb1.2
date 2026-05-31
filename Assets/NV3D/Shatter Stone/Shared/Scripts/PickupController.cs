/*******************************************************************************************
 * File: OreNode.cs
 * Author: NV3D
 * Description: Core logic for handling ore node interactions, animations, and drops.
 * Copyright © 2025 NV3D. All rights reserved.
 * This code is subject to the Unity Asset Store EULA and may not be redistributed or resold.
 *******************************************************************************************/


//This script manages behaviour and interactions with ore pickups spawned from mining the Shatter Stone ore nodes.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShatterStone

{

public class PickupController : MonoBehaviour
{
  [Header("Animation Settings")]
  [SerializeField] private float jumpDuration = 1;
  [SerializeField] private float jumpDistance = 3;
  [SerializeField] private float jumpHeight = 2;
  [SerializeField] private float spinRate = 20;
  [SerializeField] private AnimationCurve jumpCurve;

  [Space]

  [Header("Despawn Settings")]
  [SerializeField] private bool enableDespawn = true;
  [SerializeField] private float timeToDespawn = 60f;

  [Space]

  [Header("Audio Settings")]
  [SerializeField] private float volume = 1f;
  [SerializeField] private Vector2 pitchRange = new Vector2(0.95f, 1.05f);
  [SerializeField] private AudioClip[] pickupClips;



  //Internal State
  private bool jumpTrigger = true;
  private AudioSource audioSource;


  void Awake()
  {
      audioSource = GetComponent<AudioSource>();
      audioSource.spatialBlend = 1f; // 3D sound
      audioSource.playOnAwake = false;
      if (enableDespawn)
      {
        StartCoroutine(DespawnTimer());
      }

  }


// Spawn Animation

 private void Update() // Plays the jump forward animation followed by a constant spin.
  {
    if (jumpTrigger == true) {
      StartCoroutine(SpawnAnimate());
    }

    else {
      transform.Rotate (0,spinRate*Time.deltaTime,0);
    }


  }


  private IEnumerator SpawnAnimate() //Jump forward animation coroutine.
  {
    float t = 0;
    Vector3 origin = transform.position;
    Vector3 jumpTo = (transform.forward * jumpDistance) + origin;
    jumpTrigger = false;

    while (t < jumpDuration)
    {
      Vector3 heightJump = Vector3.up * Mathf.Sin ((t/jumpDuration) * Mathf.PI) * jumpHeight; //Height controlled by a Sin function
      float v = jumpCurve.Evaluate(t / jumpDuration); //Forward distance controlled with curve to ease out.
      transform.position = Vector3.Lerp(origin, jumpTo, v) + heightJump;
      t += Time.deltaTime;
      yield return null;
    }

    TrailRenderer trail = GetComponentInChildren<TrailRenderer>();
    if (trail != null)
    {
      trail.emitting = false;
      trail.Clear();
    }
  }


  //Collect Item Logic

  public void CollectItem()
  {
      PlayRandomClip(pickupClips);
      StartCoroutine(CollectDestroy(transform));
  }


  public IEnumerator CollectDestroy(Transform target)
  {
      float duration = 0.2f;
      float elapsed = 0f;

      Vector3 startPos = target.position;
      Vector3 endPos = startPos + Vector3.up * 2f;

      Vector3 startScale = target.localScale;
      Vector3 endScale = Vector3.zero;

      ParticleSystem[] particleSystems = target.GetComponentsInChildren<ParticleSystem>();
      foreach (var ps in particleSystems)
        {
          ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

      while (elapsed < duration)
      {
          float t = elapsed / duration;
          target.position = Vector3.Lerp(startPos, endPos, t);
          target.localScale = Vector3.Lerp(startScale, endScale, t);

          elapsed += Time.deltaTime;
          yield return null;
      }

      // Ensure final state is exact
      target.position = endPos;
      target.localScale = endScale;

      yield return new WaitForSeconds(5f);
      Destroy(target.gameObject);
  }


  //Audio Logic

  private void PlayRandomClip(AudioClip[] clipArray)
  {
      if (clipArray == null || clipArray.Length == 0) return;

      var clip = clipArray[Random.Range(0, clipArray.Length)];
      audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
      audioSource.PlayOneShot(clip, volume);
  }


  //Despawn Logic

  public void DespawnItem()
  {
    StartCoroutine(DespawnTimer());
  }

  private IEnumerator DespawnTimer()
  {
    yield return new WaitForSeconds(timeToDespawn);
    Destroy(gameObject);
  }
}
}
