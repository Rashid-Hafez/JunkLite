using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace RayFire
{
    [Serializable]
    public class RFDemolitionMesh
    {
        public enum ConvertType
        {
            Disabled         = 0,
            ConnectedCluster = 2,
            Connectivity     = 4
        }
        
        public int                  am;
        public int                  var;
        public float                dpf;
        public float                bias;
        public int                  cls;
        public int                  sd;
        public bool                 use;
        public bool                 cld;
        public FragSimType          sim;
        public ConvertType          cnv;
        public RFFragmentProperties prp;
        public RFRuntimeCaching     ch;
        public RayfireShatter       sht;
        
        // Non serialized
        [NonSerialized] public int       badMesh;
        [NonSerialized] public RFEngine  engine;

        /// /////////////////////////////////////////////////////////
        /// Constructor
        /// /////////////////////////////////////////////////////////

        // Constructor
        public RFDemolitionMesh()
        {
            InitValues();
            LocalReset();
            prp = new RFFragmentProperties();
            ch  = new RFRuntimeCaching();
        }
        
        // Starting values
        void InitValues()
        {
            am          = 15;
            var         = 0;
            dpf         = 0.5f;
            bias        = 0f;
            cls         = 0;
            sd          = 1;
            use         = false;
            cld         = true;
            cnv         = 0;
            sim         = FragSimType.Dynamic;
            sht         = null;
        }

        // Reset
        public void LocalReset()
        {
            badMesh     = 0;
        }
        
        // Pool Reset
        public void GlobalReset()
        {
            InitValues();
            LocalReset();
            
            prp.InitValues();
            ch.InitValues();
        }
        
        // Copy from
        public void CopyFrom (RFDemolitionMesh source)
        {
            am    = source.am;
            var   = source.var;
            dpf   = source.dpf;
            sd    = source.sd;
            bias  = source.bias;
            cls   = source.cls;
            use   = false;
            cld   = source.cld;
            cnv   = source.cnv;
            sim   = source.sim;

            prp.CopyFrom (source.prp);
            ch = new RFRuntimeCaching();

            LocalReset();
        }

        /// /////////////////////////////////////////////////////////
        /// Demolish
        /// /////////////////////////////////////////////////////////
        
        // Demolish single mesh to fragments
        public static bool DemolishMesh(RayfireRigid scr)
        {
            // Object demolition
            if (scr.objTp != ObjectType.Mesh && scr.objTp != ObjectType.SkinnedMesh)
                return true;

            // Skip if reference
            if (scr.dmlTp == DemolitionType.ReferenceDemolition)
                return true;
            
            // Already has fragments
            if (scr.HasFragments == true)
            {
                // Set parent
                RayfireMan.SetFragmentRootParent (scr.rtC);
                
                // Set tm 
                scr.rtC.position = scr.tsf.position;
                scr.rtC.rotation = scr.tsf.rotation;

                // Activate root and fragments
                scr.rtC.gameObject.SetActive (true);

                // Set demolished state
                scr.lim.demolished = true;
                
                // Skip coroutines start if Awake prefragment and Convert
                if (scr.dmlTp == DemolitionType.AwakePrefragment && scr.mshDemol.cnv != ConvertType.Disabled)
                    return true;

                // Start all coroutines
                for (int i = 0; i < scr.fragments.Count; i++)
                    scr.fragments[i].StartAllCoroutines();
                
                return true;
            }
            
            // Has unity meshes - create fragments. Multi frame create cached fragments here as well.
            if (scr.mshDemol.HasEngineAndMeshes == true)
            {
                // Start countdown
                // System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
                // stopWatch.Start();
                
                // Create fragments
                RFEngine.CreateRigidFragments (scr.mshDemol.engine, scr);
                
                // stopWatch.Stop();
                // Debug.Log(scr.name + " Create Fragments " + stopWatch.Elapsed.TotalMilliseconds + " ms.");
                
                // Set demolished state
                scr.lim.demolished = true;
                return true;
            }

            // Still has no Unity meshes - cache Unity meshes
            if (scr.mshDemol.HasEngineAndMeshes == false)
            {
                // Start countdown
                // System.Diagnostics.Stopwatch stopWatch0 = new System.Diagnostics.Stopwatch();
                // stopWatch0.Start();
                
                // Start countdown
                // System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
                // stopWatch.Start();
                
                // Cache unity meshes
                RFEngine.CacheRuntime (scr);
                
                // stopWatch.Stop();
                // Debug.Log(scr.name + " Cache Fragments " + stopWatch.Elapsed.TotalMilliseconds + " ms.");

                // Caching in progress. Stop demolition
                if (scr.mshDemol.ch.inProgress == true)
                    return false;
                
                // Fragmentation on not supported platforms. approve and set dml to none
                if (scr.mshDemol.HasEngineAndMeshes == false)
                {
                    scr.lim.demolished = false;
                    return true;
                }
                
                // Has unity meshes - create fragments
                if (scr.mshDemol.HasEngineAndMeshes == true)
                {
                    // Start countdown
                    // System.Diagnostics.Stopwatch stopWatch2 = new System.Diagnostics.Stopwatch();
                    // stopWatch2.Start();
                    
                    RFEngine.CreateRigidFragments (scr.mshDemol.engine, scr);
                    scr.lim.demolished = true;
                    
                    // stopWatch2.Stop();
                    // RayfireMan.Log(scr.name + " Create Fragments " + stopWatch2.Elapsed.TotalMilliseconds + " ms.");
                    
                    // stopWatch0.Stop();
                    // RayfireMan.Log(scr.name + " Demolition Time " + stopWatch0.Elapsed.TotalMilliseconds + " ms.");
                    
                    return true;
                }
            }

            return false;
        }
        
        /// /////////////////////////////////////////////////////////
        /// Slice
        /// /////////////////////////////////////////////////////////
        
        // SLice mesh
        public static bool SliceMesh(RayfireRigid scr)
        {
            return SliceMeshV2 (scr);
        }
        
        /// /////////////////////////////////////////////////////////
        /// V2 Slice
        /// /////////////////////////////////////////////////////////

        // SLice mesh
        public static bool SliceMeshV2(RayfireRigid scr)
        {
            // Start countdown
            // System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
            // stopWatch.Start();
            
            // Cache unity meshes
            RFEngine.CacheRuntime (scr);

            // Set force planes
            Plane forcePlane = new Plane (scr.lim.slicePlanes[1], scr.lim.slicePlanes[0]);
            
            // Clear slice planes
            scr.lim.slicePlanes.Clear();

            // Create slices
            if (scr.mshDemol.HasEngineAndMeshes == true)
                RFEngine.CreateRigidFragments (scr.mshDemol.engine, scr);
            else return false;
            
            // Set demolition 
            scr.lim.demolished = true;
            
            // Check for sliced inactive/kinematic with unyielding
            RayfireUnyielding.SetUnyieldingFragments (scr, true);
            
            // Fragments initialisation
            scr.InitMeshFragments();
            
            // Add force
            AddForce (scr, forcePlane);

            // Set children with mesh as additional fragments
            ChildrenToFragments(scr);
            
            // Skinned mesh ops TODO
            // if (scr.objTp == ObjectType.SkinnedMesh) SkinnedMeshOps(scr, forcePlane);
            
            // stopWatch.Stop();
            // Debug.Log("Slice Time " + stopWatch.Elapsed.TotalMilliseconds + " ms.");
            
            return true;
        }
        
        // Add force to slices
        static void AddForce(RayfireRigid scr, Plane forcePlane)
        {
            if (scr.lim.sliceForce != 0)
            {
                foreach (var frag in scr.fragments)
                {
                    // Skip inactive fragments
                    if (scr.lim.affectInactive == false && frag.simTp == SimType.Inactive)
                        continue;
                    
                    // Apply force
                    Vector3 closestPoint = forcePlane.ClosestPointOnPlane (frag.transform.position);
                    Vector3 normalVector = (frag.tsf.position - closestPoint).normalized;
                    frag.physics.rb.AddForce (normalVector * scr.lim.sliceForce, ForceMode.VelocityChange);

                    /* TODO force to spin fragments based on blades direction
                    normalVector = new Vector3 (-1, 0, 0);
                    frag.physics.rigidBody.AddForceAtPosition (normalVector * scr.limitations.sliceForce, closestPoint, ForceMode.VelocityChange);
                    */
                }
            }
        }
        
        /// /////////////////////////////////////////////////////////
        /// Common
        /// /////////////////////////////////////////////////////////
        
        // Set limitations properties
        public static void SetLimitationProps(RayfireRigid rfScr, int depth)
        {
            // Update depth level and amount
            rfScr.lim.currentDepth = depth + 1;
            rfScr.mshDemol.am              = (int)(rfScr.mshDemol.am * rfScr.mshDemol.dpf);
            if (rfScr.mshDemol.am < 3)
                rfScr.mshDemol.am = 3;
        }
        
        // Set custom fragment simulation type if not inherited
        public static void SetClusterSimulationType (RayfireRigid frag, SimType sim)
        {
            frag.simTp = sim;
            if (frag.clsDemol.sim != FragSimType.Inherit)
                frag.simTp = (SimType)frag.clsDemol.sim;
        }
                
        // Copy mesh renderer properties
        public static void CopyRenderer (RayfireRigid scr, MeshRenderer trg, Bounds bounds)
        {
            // Shadow casting
            if (RayfireMan.inst.advancedDemolitionProperties.sizeThreshold > 0 && 
                RayfireMan.inst.advancedDemolitionProperties.sizeThreshold > bounds.size.magnitude)
                trg.shadowCastingMode = ShadowCastingMode.Off;
            
            /*
            trg.receiveGI = scr.meshRenderer.receiveGI;
            trg.rayTracingMode            = scr.meshRenderer.rayTracingMode;
            trg.lightProbeUsage           = scr.meshRenderer.lightProbeUsage;
            trg.reflectionProbeUsage      = scr.meshRenderer.reflectionProbeUsage;
            trg.allowOcclusionWhenDynamic = scr.meshRenderer.allowOcclusionWhenDynamic;
            */
        }
        
        /// /////////////////////////////////////////////////////////
        /// Precache and Prefragment
        /// /////////////////////////////////////////////////////////  
        
        // Precache meshes at awake
        public static void Awake(RayfireRigid scr)
        {
            // Not mesh
            if (scr.objTp != ObjectType.Mesh)
                return;
                
            // Precache
            if (scr.dmlTp == DemolitionType.AwakePrecache)
                PreCache(scr);

            // Precache and prefragment
            if (scr.dmlTp == DemolitionType.AwakePrefragment)
            {
                // Disable runtime caching
                scr.mshDemol.ch.tp = CachingType.Disabled;
                
                PreCache(scr);
                Prefragment(scr);
            }
        }

        // PreCache meshes
        static void PreCache(RayfireRigid scr)
        {
            // Save and disable bias
            float bias = scr.mshDemol.bias;
            scr.mshDemol.bias = 0;
                
            // Cache frag meshes
            RFEngine.CacheRuntime (scr);

            // Restore bias
            scr.mshDemol.bias = bias;
        }
        
        // Predefine fragments
        static void Prefragment(RayfireRigid scr)
        {
            // Delete existing
            scr.DeleteFragments();

            // Create fragments from cache
            RFEngine.CreateRigidFragments (scr.mshDemol.engine, scr);
                
            // Stop
            if (scr.HasFragments == false)
            {
                scr.dmlTp = DemolitionType.None;
                return;
            }
            
            // Set physics properties
            for (int i = 0; i < scr.fragments.Count; i++)
            {
                scr.fragments[i].SetComponentsBasic();
                scr.fragments[i].SetComponentsPhysics();
                scr.fragments[i].SetObjectType();

                // Increment demolition depth. Disable if last
                scr.fragments[i].lim.currentDepth = 1;
                if (scr.lim.depth == 1)
                    scr.fragments[i].dmlTp = DemolitionType.None;
            }
            
            // Copy Uny state to fragments in case object has Uny components
            RayfireUnyielding.SetUnyieldingFragments(scr, false);
            
            // Clusterize awake fragments
            SetupRuntimeConnectedCluster (scr, true);
            
            // Deactivate fragments root
            if (scr.rtC != null)
                scr.rtC.gameObject.SetActive (false);
            
            // Setup awake connectivity
            SetupRuntimeConnectivity(scr, true);
        }
        
        // Clusterize runtime fragments
        public static bool SetupRuntimeConnectedCluster (RayfireRigid rigid, bool awake)
        {
            // Clusterize disabled
            if (rigid.mshDemol.cnv != ConvertType.ConnectedCluster)
                return false;
            
            // Skip if Runtime init and fragments already clusterized in awake.
            if (rigid.dmlTp == DemolitionType.AwakePrefragment && rigid.lim.demolished == true)
                return false;
            
            // Not mesh demolition
            if (rigid.objTp != ObjectType.Mesh)
                return false;
            
            // Not runtime
            if (rigid.dmlTp == DemolitionType.None || 
                rigid.dmlTp == DemolitionType.ReferenceDemolition)
                return false;

            // No fragments
            if (rigid.HasFragments == false)
                return false;

            // Create Connected cluster Rigid
            RayfireRigid clsRigid = rigid.rtC.gameObject.AddComponent<RayfireRigid>();

            // Copy properties
            rigid.CopyPropertiesTo (clsRigid);
            
            // Copy particles
            RFPoolingParticles.CopyParticlesRigid (rigid, clsRigid);  

            // Destroy particles on fragments
            for (int i = rigid.fragments.Count - 1; i >= 0; i--)
            {
                // Destroy Debris/Dust for all fragments
                if (rigid.fragments[i].HasDebris)
                    for (int d = rigid.fragments[i].debrisList.Count - 1; d >= 0; d--)
                        Object.Destroy (rigid.fragments[i].debrisList[d]);
                if (rigid.fragments[i].HasDust)
                    for (int d = rigid.fragments[i].dustList.Count - 1; d >= 0; d--)
                        Object.Destroy (rigid.fragments[i].dustList[d]);
            }

            // Set properties
            clsRigid.objTp            = ObjectType.ConnectedCluster;
            clsRigid.dmlTp            = DemolitionType.Runtime;
            clsRigid.clsDemol.cluster = new RFCluster();
            
            // Init
            clsRigid.Initialize(); 

            // Set uny states and sim
            RayfireUnyielding[] unyArray = rigid.GetComponents<RayfireUnyielding>();
            if (unyArray.Length > 0)
                for (int i = 0; i < unyArray.Length; i++)
                    if (unyArray[i].enabled == true)
                        RayfireUnyielding.ClusterOverlap(unyArray[i], clsRigid);
            
            // Stop if awake connected cluster
            if (awake == true)
            {
                DestroyComponents (rigid.fragments);
                return true;
            }
            
            // Set contact point for demolition
            clsRigid.lim.contactPoint   = rigid.lim.contactPoint;
            clsRigid.lim.contactNormal  = rigid.lim.contactNormal;
            clsRigid.lim.contactVector3 = rigid.lim.contactVector3;

            // Inherit velocity
            clsRigid.physics.velocity    = rigid.physics.velocity;
            
            // Set velocity after demolition
            RFPhysic.SetClusterVelocity (clsRigid.physics, rigid.physics, 1f);
            
            // Demolish cluster and get solo shards
            List<RFShard> detachShards = RFDemolitionCluster.DemolishConnectedCluster (clsRigid);
  
            // No Shards to detach
            if (detachShards == null || detachShards.Count == 0)
            {
                DestroyComponents (rigid.fragments);
                return true;
            }

            // Get has for all detached objects to keep their Rigid and rigidbody. Should be used even if no detach shards.
            HashSet<Transform> detachTms = new HashSet<Transform>();
            for (int i = 0; i < detachShards.Count; i++)
                detachTms.Add (detachShards[i].tm);
           
            // Destroy fragments rigid, rigidbody for NOT detached shards
            for (int i = rigid.fragments.Count - 1; i >= 0; i--)
            {
                // Destroy rb, rigid
                if (detachTms.Contains (rigid.fragments[i].tsf) == false)
                {
                    Object.Destroy (rigid.fragments[i].physics.rb);
                    Object.Destroy (rigid.fragments[i]);
                    rigid.fragments.RemoveAt (i);
                }
            }

            // TODO add main and child clusters to fragments list. get them in scr.fragments
            
            // Delete if cluster was completely demolished
            if (clsRigid.lim.demolished == true)
                RayfireMan.DestroyFragment (clsRigid, null);

            return true;
        }

        // Destroy fragments rigid, rigidbody
        static void DestroyComponents(List<RayfireRigid> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Object.Destroy (list[i].physics.rb);
                Object.Destroy (list[i]);
            }
        }

        // Setup Connectivity, Unyielding and MeshRoot
        public static void SetupRuntimeConnectivity(RayfireRigid scr, bool awake)
        {
            // Connectivity disabled
            if (scr.mshDemol.cnv != ConvertType.Connectivity)
                return;
            
            // Component check
            scr.act.cnt = scr.GetComponent<RayfireConnectivity>();
            
            // Debug message and return
            if (scr.act.cnt == null)
            {
                RayfireMan.Log (RFLog.rig_dbgn + scr.name + RFLog.rig_act1 + scr.mshDemol.cnv.ToString() + RFLog.rig_act2, scr.gameObject);
                scr.mshDemol.cnv = ConvertType.Disabled;
                return;
            }
            
            // Add meshroot Rigid
            RayfireRigid mRoot = scr.rtC.gameObject.GetComponent<RayfireRigid>();
            
            // Skip if Runtime init and fragments already connected in awake.
            if (mRoot != null)
                return;

            mRoot = scr.rtC.gameObject.AddComponent<RayfireRigid>();

            // Set MeshRoot properties
            scr.CopyPropertiesTo (mRoot);
            mRoot.init    = RayfireRigid.InitType.AtStart;
            mRoot.objTp   = ObjectType.MeshRoot;
            mRoot.dmlTp   = DemolitionType.None;
            mRoot.simTp   = (SimType)(int)scr.mshDemol.sim;
            mRoot.act.con = true;
            
            /*
            put SetUnyieldingFragments after this method
            Debug.Log (mRoot.simTp);
            // Set sim type for root as well.
            if (mRoot.mshDemol.sim != FragSimType.Inherit)
                mRoot.simTp = (SimType)scr.mshDemol.sim;
            
            Debug.Log (mRoot.simTp);
            */
            
            // Set Connectivity activation for fragments
            for (int i = 0; i < scr.fragments.Count; i++)
                scr.fragments[i].act.con = true;

            // Add Connectivity
            RayfireConnectivity mRootConnectivity = scr.rtC.gameObject.AddComponent<RayfireConnectivity>();

            // Copy Connectivity properties
            RayfireConnectivity.CopyTo (scr.act.cnt, mRootConnectivity);

            // Activate to initialize
            scr.rtC.gameObject.SetActive (true);

            // DeActivate in awake / init at runtime
            if (awake == true)
                scr.rtC.gameObject.SetActive (false);
            else
            {
                mRoot.Initialize();
            }
        }
        
        /// /////////////////////////////////////////////////////////
        /// Children ops
        /// /////////////////////////////////////////////////////////  
        
        // Set children with mesh as additional fragments
        public static void ChildrenToFragments(RayfireRigid scr)
        {
            // Not for clusters
            if (scr.IsCluster == true)
                return;

            // Disabled
            if (scr.mshDemol.cld == false)
                return;
            
            // No children
            if (scr.tsf.childCount == 0)
                return;
            
            // Iterate children TODO precache in awake and use now. Set init type to by method at awake.
            Transform child;
            int childCount = scr.tsf.childCount;
            for (int i = childCount - 1; i >= 0; i--)
            {
                // Get child
                child = scr.tsf.GetChild (i);

                // Skip if has no mesh
                if (child.GetComponent<MeshFilter>() == false)
                    continue;

                // Set parent to main fragments root
                child.parent = scr.rtC;
                
                // Get Already applied Rigid
                RayfireRigid childScr = child.GetComponent<RayfireRigid>();

                // Add new if has no. Copy properties
                if (childScr == null)
                {
                    childScr = child.gameObject.AddComponent<RayfireRigid>();
                    childScr.init = RayfireRigid.InitType.ByMethod;
                    scr.CopyPropertiesTo (childScr);
                    
                    // Enable use shatter
                    childScr.mshDemol.sht = child.GetComponent<RayfireShatter>();
                    if (childScr.mshDemol.sht != null)
                        childScr.mshDemol.use = true;
                }
                
                // Set custom fragment simulation type if not inherited
                RFPhysic.SetFragmentSimulationType (childScr, scr.simTp);

                // Init
                childScr.Initialize();
                
                // Update depth level and amount
                childScr.lim.currentDepth = scr.lim.currentDepth + 1;
                
                // Collect
                scr.fragments.Add (childScr);
            }
        }
        
        /// /////////////////////////////////////////////////////////
        /// Getters
        /// /////////////////////////////////////////////////////////

        // Get seed
        public int Seed { get {
            if (sd == 0)
                return Random.Range (0, 100);
            return sd;
        }}
        
        // Get amount with variation
        public int Amount { get {
            if (var > 0)
                return am + Random.Range (0, am * var / 100);
            return am;
        }}
        
        // Get use shatter state
        public bool UseShatter { get
        {
            return use == true && sht != null;
        }}
        
        // Get use shatter state
        public bool HasEngineAndMeshes { get
        {
            return engine != null && engine.HasUtilFragMeshes == true;
        }}
        
    }
}