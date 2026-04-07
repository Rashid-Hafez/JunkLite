using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

namespace RayFire
{

/*


 https://docs.unity3d.com/ScriptReference/SkinnedMeshRenderer.html
 https://docs.unity3d.com/ScriptReference/Mesh.html
 https://docs.unity3d.com/ScriptReference/Mesh-bindposes.html


 Slice
    Copy bones transforms
    Create skinned meshes for bones
    Destroy bones without mesh
    Add rigidbodies / Set existing to dynamic
 Sim





DestroyImmediate in RFEngine to Destroy for Build usage, test in build
Rigid skinned demolition velocity inherit to fragments with enabled Petrify
Rigid skinned demolition animation reset if demolish quickly after start
Slice Targets do not slice two objects instantly




 */


    [Serializable]
    public class RFRag
    {
        public int             sides;
        public List<RFSkin>    rfSkins = new List<RFSkin>();
        public List<Transform> bones   = new List<Transform>();
        public List<RFChain>   chains  = new List<RFChain>();
        public List<RFBone>    rfBones = new List<RFBone>();
        
        /// /////////////////////////////////////////////////////////
        /// Static
        /// /////////////////////////////////////////////////////////
        
        // Set RFSkins and bones list
        public static void SetSkinBones (RFRag rag, SkinnedMeshRenderer[] sks)
        {
            BoneWeight         bw;
            BoneWeight[]       weights;
            Transform          bone;
            HashSet<Transform> hash = new HashSet<Transform>();
            
            rag.bones.Clear();
            rag.rfBones.Clear();
            rag.rfSkins.Clear();
            
            for (int s = 0; s < sks.Length; s++)
            {
                // Vars
                RFSkin rfSkin = new RFSkin();
                rfSkin.id   = s;
                rfSkin.skin = sks[s];
                
                weights = sks[s].sharedMesh.boneWeights;
                
                // Iterate skins
                for (int w = 0; w < weights.Length; w++)
                {
                    bw = weights[w];
                    if (bw.weight0 > 0)
                    {
                        bone = rfSkin.skin.bones[bw.boneIndex0];
                        if (hash.Contains(bone) == false)
                        {
                            hash.Add (bone);
                            rag.bones.Add (bone);
                            rag.rfBones.Add (new RFBone(bone));
                            rfSkin.boneIds.Add (rag.bones.Count - 1);
                        }
                    }
                    if (bw.weight1 > 0)
                    {
                        bone = rfSkin.skin.bones[bw.boneIndex1];
                        if (hash.Contains(bone) == false)
                        {
                            hash.Add (bone);
                            rag.bones.Add (bone);
                            rag.rfBones.Add (new RFBone(bone));
                            rfSkin.boneIds.Add (rag.bones.Count - 1);
                        }
                    }
                    if (bw.weight2 > 0)
                    {
                        bone = rfSkin.skin.bones[bw.boneIndex2];
                        if (hash.Contains(bone) == false)
                        {
                            hash.Add (bone);
                            rag.bones.Add (bone);
                            rag.rfBones.Add (new RFBone(bone));
                            rfSkin.boneIds.Add (rag.bones.Count - 1);
                        }
                    }
                    if (bw.weight3 > 0)
                    {
                        bone = rfSkin.skin.bones[bw.boneIndex3];
                        if (hash.Contains(bone) == false)
                        {
                            hash.Add (bone);
                            rag.bones.Add (bone);
                            rag.rfBones.Add (new RFBone(bone));
                            rfSkin.boneIds.Add (rag.bones.Count - 1);
                        }
                    }
                }
                
                // Collect skin
                rag.rfSkins.Add (rfSkin);
            }
            
            // Set bones Id and live state
            for (int i = 0; i < rag.rfBones.Count; i++)
            {
                rag.rfBones[i].id = i;
                if (rag.rfBones[i].tm.GetComponent<RayfireUnyielding>() != null)
                    rag.rfBones[i].live = true;
            }
            
            // Set bones children and parent id
            SetParentChildId (rag, hash);
        }
        
        // Set bones children and parent id
        static void SetParentChildId(RFRag rag, HashSet<Transform> hash)
        {
            Transform child;
            Transform parent;
            for (int b = 0; b < rag.rfBones.Count; b++)
            {
                // Bone has children
                if (rag.rfBones[b].tm.childCount > 0)
                {
                    // Iterate all children
                    for (int c = 0; c < rag.rfBones[b].tm.childCount; c++)
                    {
                        // Get child transform
                        child = rag.rfBones[b].tm.GetChild (c);
                        
                        // Child among bones
                        if (hash.Contains (child) == true)
                        {
                            // Find child rfbone in rfbones list
                            for (int j = 0; j < rag.rfBones.Count; j++)
                            {
                                // Skip self
                                if (b != j)
                                {
                                    // Find child rfbone
                                    if (rag.rfBones[j].tm == child)
                                    {
                                        // Collect child id
                                        rag.rfBones[b].childIds.Add (rag.rfBones[j].id);

                                        // Set parent id for child
                                        rag.rfBones[j].parentId = rag.rfBones[b].id;

                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                // Has no children. last bone
                else
                {
                    // Has parent
                    parent = rag.rfBones[b].tm.parent;
                    if (parent != null)
                    {
                        // Parent among RFBones
                        if (hash.Contains (parent) == true)
                        {
                            // Find parent RFBone in RFBones list
                            for (int j = 0; j < rag.rfBones.Count; j++)
                            {
                                // Skip self
                                if (b != j)
                                {
                                    // Find parent RFBones
                                    if (rag.rfBones[j].tm == parent)
                                    {
                                        // Set parent id for child
                                        rag.rfBones[b].parentId = rag.rfBones[j].id;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        
        // Set sides
        public static void SetSkinSides(RFRag rag, Plane plane)
        {
            List<int> sideList = new List<int>(2);
            for (int i = 0; i < rag.rfSkins.Count; i++)
            {
                rag.rfSkins[i].sideId = plane.GetSide (rag.rfSkins[i].skin.bounds.center) == true ? 0 : 1;
                if (sideList.Contains (rag.rfSkins[i].sideId) == false)
                    sideList.Add (rag.rfSkins[i].sideId);
            }
            
            // Set amount of sides
            rag.sides = sideList.Count;
        }
        
        // Set chains
        public static void SetBoneChains(RFRag rag)
        {
            for (int i = 0; i < rag.sides; i++)
            {
                // Get skin indexes with the same side id
                List<int> sideSkinIndexes = rag.GetSkinsIndexesBySideId (i);
                
                            Debug.Log ("------ sideSkinIndexes " + sideSkinIndexes.Count);
                            for (int j = 0; j < sideSkinIndexes.Count; j++)
                                 Debug.Log (rag.rfSkins[sideSkinIndexes[j]].skin.name, rag.rfSkins[sideSkinIndexes[j]].skin.gameObject);
                            
                // Collect bones indexes by one side using collected skin indexes
                List<int> sideBoneIndexes = rag.GetBonesIndexesBySkinIndexes (sideSkinIndexes);
                
                            Debug.Log ("------ sideBoneIndexes " + sideBoneIndexes.Count);
                            for (int j = 0; j < sideBoneIndexes.Count; j++)
                                Debug.Log (rag.rfBones[sideBoneIndexes[j]].tm.name, rag.rfBones[sideBoneIndexes[j]].tm.gameObject);
                        
                // Get side bone chains
                List<RFChain> sideChains = rag.GetBoneChains (sideBoneIndexes);
                
                
                // TODO check live state by bones and set live state for skins
                
                // TODO set skin bones to array only with bones from side bones
                
                
                // Collect all bone chains
                rag.chains.AddRange (sideChains);
            }
        }

        // Get skin indexes with the same side id
        List<int> GetSkinsIndexesBySideId(int sideId)
        {
            List<int> sideSkinsIds = new List<int>();
            for (int s = 0; s < rfSkins.Count; s++)
                if (rfSkins[s].sideId == sideId)
                    sideSkinsIds.Add (rfSkins[s].id);
            return sideSkinsIds;
        }
        
        // Get bones indexes by one side using collected skin indexes
        List<int> GetBonesIndexesBySkinIndexes(List<int> skinIds)
        {
            List<int>    bns  = new List<int>();
            HashSet<int> hash = new HashSet<int>();
            for (int s = 0; s < skinIds.Count; s++)
            {
                int skinId = skinIds[s];
                for (int b = 0; b < rfSkins[skinId].boneIds.Count; b++)
                {
                    int boneIndex = rfSkins[skinId].boneIds[b];
                    if (hash.Contains(boneIndex) == false)
                    {
                        bns.Add (boneIndex);
                        hash.Add (boneIndex);
                    }
                }
            }
            return bns;
        }
        
        // Separate list of bones to group of separated connected bone chains
        List<RFChain> GetBoneChains(List<int> boneIds)
        {
            List<RFChain> rfChains  = new List<RFChain>();
            List<int>     check     = new List<int>();
            HashSet<int>  chainHash = new HashSet<int>();
            HashSet<int>  bonesHash = new HashSet<int>();
            for (int b = 0; b < boneIds.Count; b++)
                bonesHash.Add (boneIds[b]);
            
                        /*
                        Debug.Log ("============ GetBoneChains " + ids.Count);
                        for (int j = 0; j < ids.Count; j++)
                            Debug.Log (ids[j] + "  " + rfBones[ids[j]].tm.name, rfBones[ids[j]].tm.gameObject);
                        Debug.Log ("============  ");
                        */
            
            // Start amount
            while (boneIds.Count > 0)
            {
                check.Clear();
                check.Add (boneIds[0]);
                
                chainHash.Clear();
                chainHash.Add (boneIds[0]);

                RFChain chain = new RFChain();
                chain.boneIds.Add (boneIds[0]);
                
                // Collect by neibs
                while (check.Count > 0)
                {
                    // Collect parent
                    int parentInd = rfBones[check[0]].parentId;
                    if (parentInd >= 0)
                    {
                        // Parent among input bones
                        if (bonesHash.Contains (parentInd) == true)
                        {
                            // Parent is not yet added in local chain
                            if (chainHash.Contains (parentInd) == false)
                            {
                                check.Add (parentInd);
                                chainHash.Add (parentInd);
                                chain.boneIds.Add (parentInd);
                            }
                        }
                    }
                    
                    // Collect children
                    List<int> childrenInd = rfBones[check[0]].childIds;
                    if (childrenInd.Count > 0)
                    {
                        for (int c = 0; c < childrenInd.Count; c++)
                        {
                            int childId = childrenInd[c];
                            
                            // Child among input bones
                            if (bonesHash.Contains (childId) == true)
                            {
                                // Already collected in chain
                                if (chainHash.Contains (childId) == false)
                                {
                                    check.Add (childId);
                                    chainHash.Add (childId);
                                    chain.boneIds.Add (childId);
                                }
                            }
                        }
                    }
                    
                    // Remove checked
                    check.RemoveAt(0);
                }
                
                // Set parent ids for chain bones. Do not consider parent not from chain
                int parentId;
                for (int i = 0; i < chain.boneIds.Count; i++)
                {
                    parentId = rfBones[chain.boneIds[i]].parentId;
                    if (chainHash.Contains (parentId) == false)
                        chain.parentIds.Add (-1);
                    else
                        chain.parentIds.Add (parentId); 
                }
                
                // Set live state TODO improve, check weight threshold to avoid considering far Uny bone, cache all uny bones in awake
                chain.live = GetChainLiveState (chain.boneIds);
                
                // Collect new chain
                rfChains.Add (chain);
                
                // Remove collected bones
                for (int i = boneIds.Count - 1; i >= 0; i--)
                    if (chainHash.Contains(boneIds[i]) == true)
                        boneIds.RemoveAt(i);
            }
            
            return rfChains;
        }
        
        // Get live state for side by bones unyielding component
        bool GetChainLiveState (List<int> bonesIds)
        {
            for (int i = 0; i < bonesIds.Count; i++)
                if (rfBones[bonesIds[i]].live == true)
                    return true;
            return false;
        }
        
        // Get live state for RFSkin by bones
        bool GetSkinLiveState (int skinId)
        {
            for (int b = 0; b < rfSkins[skinId].boneIds.Count; b++)
                if (rfBones[rfSkins[skinId].boneIds[b]].live == true)
                    return true;
            return false;
        }
        
        // Get main parent bone in chain
        public Transform MainBone(RFChain chain)
        {
            for (int i = 0; i < chain.parentIds.Count; i++)
                if (chain.parentIds[i] < 0)
                    return rfBones[chain.boneIds[i]].tm;
            return null;
        }
        
        /// /////////////////////////////////////////////////////////
        /// Getters
        /// /////////////////////////////////////////////////////////

        // Check if all sides dead
        public bool HasAlive()
        {
            for (int i = 0; i < chains.Count; i++)
                if (chains[i].live == true)
                    return true;
            return false;
        }

        // Check if all sides dead
        public bool AllAlive()
        {
            for (int i = 0; i < chains.Count; i++)
                if (chains[i].live == false)
                    return false;
            return true;
        }
        
        // Check if all sides dead
        public bool AllDead()
        {
            for (int i = 0; i < chains.Count; i++)
                if (chains[i].live == true)
                    return false;
            return true;
        }
        
    }
    
    [Serializable]
    public class RFSkin
    {
        public int                 id;
        public int                 sideId;
        public List<int>           boneIds = new List<int>();
        public SkinnedMeshRenderer skin    = new SkinnedMeshRenderer();
        public bool                live    = false;
    }

    [Serializable]
    public class RFChain
    {
        public bool      live      = true;
        public List<int> boneIds   = new List<int>();
        public List<int> parentIds = new List<int>();
        
        
        
        
        
        
        public List<Transform> oldBones  = new List<Transform>();
        public List<Transform> newbones  = new List<Transform>();



        // Duplicate chain bones
        public static void DuplicateChain(RFRag rag, RFChain chain, Transform parent)
        {
            // Create copy chain
            List<Transform> newBones = new List<Transform>();

            for (int c = 0; c < chain.boneIds.Count; c++)
            {
                int    rfBoneId = chain.boneIds[c];
                RFBone rfBone   = rag.rfBones[rfBoneId];
                GameObject bone = new GameObject(rfBone.tm.name);
                bone.transform.parent = parent;
                bone.transform.SetPositionAndRotation (rfBone.tm.position, rfBone.tm.rotation);
                newBones.Add (bone.transform);
            }
            
            /*
            // Set parent
            for (int c = 0; c < newBones.Count; c++)
            {
                int parentId = chain.parentIds[c];
                if (parentId >= 0)
                    newBones[c].parent = newBones[parentId];
                
                // Swap bone in chain
                chain.newbones.Add (newBones[c]);
            }
            */
        }
    }

    [Serializable]
    public class RFBone
    {
        public int       id;
        public Transform tm;
        public int       parentId;
        public List<int> childIds;
        public bool      live;
        
        
        public Transform       parent;
        public List<Transform> childTms; // Remove and use ids with list
        public Transform       dummy;
        public float           sizeAvg;
        public float           sizeRaw;
        public Vector3         center;
        public bool            last;
        public bool            excluded;
        public Rigidbody       rBody;
        public bool            rBodyOwn;
        

        public float[]  distance;
        public Collider collider;
        public float    radius;

        public int side;

        // Constructor
        public RFBone(Transform Tm)
        {
            tm       = Tm;
            parentId = -1;
            childTms = new List<Transform>();
            childIds = new List<int>();
            live      = false;
        }
    }

    public class RayfireRagdoll : MonoBehaviour
    {
        // Preview
        public bool showNodes  = true;
        public bool showConns  = true;
        public bool showSizes  = true;
        public bool showCenter = true;

        // Properties
        public float radius        = 1;
        public float sizeThreshold = 0.4f;

        // Rigidbody props
        public float mass = 70f;

        // Joint props
        public float twist  = 0f;
        public float swing1 = 30f;
        public float swing2 = 40f;

        // Lists
        public List<RFBone>    rfBones;
        public List<Collider>  colliders;
        public List<Rigidbody> rigidBodies;
        public List<Joint>     joints;

        // private
        public Transform             root;
        
        HashSet<Transform>           bonesHash;
        public GameObject slicePlane;
        
        
        public SkinnedMeshRenderer[] skins;
        public List<Transform>       bones;
        

        /// /////////////////////////////////////////////////////////
        /// 
        /// /////////////////////////////////////////////////////////

        [ContextMenu("Test")]
        public void Test()
        {
            
            
/*
            
            SkinnedMeshRenderer[] sks = GetComponentsInChildren<SkinnedMeshRenderer> (false);
            List<Transform>       bns = GetSkinBonesByWeight (sks.ToList());
            
            Debug.Log ("ragdoll");
            Debug.Log (name);
            Debug.Log (bns.Count);
            for (int i = 0; i < bns.Count; i++)
                Debug.Log (bns[i].name, bns[i].gameObject);
            
            List<RFChain> boneChains = GetBoneChains (bns);
            
*/
            
            
            // SetBoneStructure();
            
            
            /*
            SkinnedMeshRenderer skin = GetComponent<SkinnedMeshRenderer>();
            Transform[]         bns  = skin.bones;

            Debug.Log ("bones all");
            Debug.Log (bns.Length);
            for (int i = 0; i < bns.Length; i++)
                if (bns[i] != null)
                    Debug.Log (i.ToString() + " " + bns[i].name, bns[i].gameObject);

            Debug.Log ("rootBone");
            Debug.Log (skin.rootBone);

            Debug.Log ("bindposes");
            Debug.Log (skin.sharedMesh.bindposes.Length);



            Debug.Log ("boneWeights per vertex");
            Debug.Log (skin.sharedMesh.boneWeights.Length); // per vertex
            Debug.Log (skin.sharedMesh.boneWeights[300].weight0);
            Debug.Log (skin.sharedMesh.boneWeights[300].boneIndex0);
            Debug.Log (skin.sharedMesh.boneWeights[300].weight1);
            Debug.Log (skin.sharedMesh.boneWeights[300].boneIndex1);



            // Get all sliced skin meshes
            skins = GetComponentsInChildren<SkinnedMeshRenderer> (false);

            // Get slice plane
            Plane plane = new Plane (slicePlane.transform.up, slicePlane.transform.position);

            // Get sides of skins relative to slice plane
            bool[] skinSides = GetSlicedSkinSides (skins, plane);

            // Collect bones on two sides, shared bones has value 2
            int[] boneSides = GetBonesSides (rfBones, plane);


            for (int i = 0; i < rfBones.Count; i++)
            {
                //Debug.Log (rfBones[i].tm.name + " Side: " + boneSides[i], rfBones[i].tm.gameObject);
            }
            */

            // Set bone chains by sides

            // ?Flatten bones

            // Duplicate shared bones, ignore duplicated colliders

            // Remove joint for shared bone/parent/chil 

            // Rebind skinned meshes on one side to duplicated bones 

            // Remove joint from main starting bone in all chains to prevent freeze in air


            // Remove animator
        }

        /// /////////////////////////////////////////////////////////
        /// Skin sides
        /// /////////////////////////////////////////////////////////
        
        // Destroy skins from opposite sides
        static void DestroyOppositeSkins(RayfireRagdoll ragOrig, RayfireRagdoll ragInst, bool[] skinSides)
        {
            for (int i = 0; i < ragOrig.skins.Length; i++)
                Object.Destroy (skinSides[i] == false 
                    ? ragOrig.skins[i].gameObject 
                    : ragInst.skins[i].gameObject); 
        }
        
        // TODO change to int, 0/1 sides, 2 both sides
        int[] GetInputSkinSides(SkinnedMeshRenderer[] sks, Plane plane)
        {
            int[] skinSides = new int[sks.Length];
            return skinSides;
        }
        
        // TODO change to int, 0/1 sides, 2 both sides
        public int[] GetBonesSides(List<RFBone> boneList, Plane plane)
        {
            // Set array
            int[] boneSides = new int[boneList.Count];

            // Iterate all bones
            for (int i = 0; i < boneList.Count; i++)
            {
                // Already set by children
                if (boneSides[i] == 2)
                    continue;
                
                // Get origin side
                bool origSide = plane.GetSide (boneList[i].tm.position);
                boneSides[i] = BoolToInt (origSide);
                
                // Get child side
                if (boneList[i].childTms.Count > 0)
                {
                    for (int j = 0; j < boneList[i].childTms.Count; j++)
                    {
                        bool childSide = plane.GetSide (boneList[i].childTms[j].position);

                        // Original bone and child on different sides
                        if (origSide != childSide)
                        {
                            boneSides[i]                       = 2;
                            boneSides[boneList[i].childIds[j]] = 2;
                            break;
                        }
                    }
                }
                
                // Check for dummy bone slice
                else if (boneList[i].dummy != null)
                {
                    bool dummySide = plane.GetSide (boneList[i].dummy.position);

                    // Original bone and dummy on different sides
                    if (origSide != dummySide)
                    {
                        boneSides[i] = 2;
                        break;
                    }
                }
            }
            
            return boneSides;
        }
        
        /// /////////////////////////////////////////////////////////
        /// Bone structure
        /// /////////////////////////////////////////////////////////

        // Setup bone structure
        public void SetBoneStructure()
        {
            // Set root
            root = transform;

            // Get all active skins
            skins = root.GetComponentsInChildren<SkinnedMeshRenderer> (false);
            if (skins.Length == 0)
            {
                Debug.Log ("No Skinned Meshes");
                return;
            }

            // Collect all rfbones
            rfBones   = new List<RFBone>();
            bonesHash = new HashSet<Transform>();

            // Collect rfbones
            foreach (var sk in skins)
            {
                // Collect if uniq in list
                foreach (Transform bone in sk.bones)
                    if (bonesHash.Contains (bone) == false)
                        rfBones.Add (new RFBone (bone));

                // Update hash
                bonesHash.UnionWith (sk.bones);
            }

            // TODO collect binded skins in RFbone

            // Set Id
            for (int i = 0; i < rfBones.Count; i++)
                rfBones[i].id = i;

            // Set bones children
            foreach (RFBone rfbone in rfBones)
            {
                if (rfbone.tm.childCount > 0)
                {
                    for (int i = rfbone.tm.childCount - 1; i >= 0; i--)
                    {
                        Transform child = rfbone.tm.GetChild (i);
                        if (bonesHash.Contains (child) == true)
                        {
                            rfbone.childTms.Add (child);

                            // Get child id
                            for (int j = 0; j < rfBones.Count; j++)
                            {
                                if (rfBones[j].tm == child)
                                {
                                    rfbone.childIds.Add (rfBones[j].id);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            // Set parent
            foreach (RFBone rfbone in rfBones)
                if (bonesHash.Contains (rfbone.tm.parent) == true)
                {
                    rfbone.parent = rfbone.tm.parent;
                }

            // Set dummies
            foreach (RFBone rfbone in rfBones)
                if (rfbone.childTms.Count == 0)
                    if (rfbone.tm.childCount == 1)
                        rfbone.dummy = rfbone.tm.GetChild (0);

            // Set size
            foreach (RFBone rfbone in rfBones)
            {
                rfbone.sizeAvg = 0f;
                rfbone.sizeRaw = 0f;

                // Size by children
                if (rfbone.childTms.Count > 0)
                {
                    foreach (Transform child in rfbone.childTms)
                        rfbone.sizeRaw += child.localPosition.magnitude;
                    rfbone.sizeAvg = rfbone.sizeRaw / rfbone.childTms.Count;
                }

                // Size by dummy
                if (rfbone.childTms.Count == 0)
                {
                    rfbone.sizeRaw = rfbone.dummy.localPosition.magnitude;
                    rfbone.sizeAvg = rfbone.sizeRaw;
                }
            }

            // Set center
            foreach (RFBone rfbone in rfBones)
            {
                rfbone.center = Vector3.zero;

                // Size by children
                if (rfbone.childTms.Count > 0)
                {
                    foreach (Transform child in rfbone.childTms)
                        rfbone.center += child.localPosition / 2f;
                    rfbone.center /= (float)rfbone.childTms.Count;
                }

                // Size by dummy
                if (rfbone.childTms.Count == 0)
                    rfbone.center = rfbone.dummy.localPosition / 2f;
            }

            // Set uny
            foreach (RFBone rfbone in rfBones)
            {
                RayfireUnyielding uny = rfbone.tm.GetComponent<RayfireUnyielding>();
                if (uny != null)
                    rfbone.live = true;
            }

            // Set exclusion
            SetExclusionBySize();

            bonesHash = null;
        }

        // Set exclusion by bone size
        void SetExclusionBySize()
        {
            if (HasRFBones == false)
                return;

            // Reset states
            foreach (RFBone rfbone in rfBones)
            {
                rfbone.last     = false;
                rfbone.excluded = false;
            }

            // Set last state
            SetLastState();

            bool needNewCheck;
            do
            {
                // Reset new cycle state
                needNewCheck = false;

                // Get iterate check state
                foreach (RFBone rfbone in rfBones)
                {
                    if (rfbone.last == true && rfbone.excluded == false)
                    {
                        if (rfbone.sizeAvg < sizeThreshold)
                        {
                            rfbone.excluded = true;
                            needNewCheck    = true;
                        }
                    }
                }

                // Set Last state for parent bone if has no
                if (needNewCheck == true)
                    SetLastState();

            } while (needNewCheck == true);
        }

        // Set last bone state
        void SetLastState()
        {
            foreach (RFBone rfbone in rfBones)
            {
                // Skip excluded bones, they can't be last
                if (rfbone.excluded == true)
                    continue;

                // Check bones with children
                if (rfbone.childIds.Count > 0)
                {
                    // Check how many children excluded
                    int lastBones = 0;
                    for (int i = 0; i < rfbone.childIds.Count; i++)
                        if (rfBones[rfbone.childIds[i]].excluded == true)
                            lastBones++;

                    // All children excluded, set last state
                    if (lastBones >= rfbone.childIds.Count)
                        rfbone.last = true;
                }

                // Bone with dummy always last
                else if (rfbone.dummy != null)
                    rfbone.last = true;
            }
        }

        // Reset structure
        public void ResetBoneStructure()
        {
            rfBones = null;
        }

        // Exclude bones by distance starting from dummies
        public void ChangedSize()
        {
            if (rfBones == null || rfBones.Count == 0)
                return;

            // Change exclude state
            SetExclusionBySize();

            // TODO interactive
            // Remove/add colliders
            // Remove/add rigidbodies
            // Remove/add joints
        }

        /// /////////////////////////////////////////////////////////
        /// Colliders
        /// /////////////////////////////////////////////////////////

        // Create colliders for each bone
        public void CreateColliders()
        {
            if (HasRFBones == false)
                return;

            // Destroy ragdoll colliders
            DestroyColliders();

            colliders = new List<Collider>();
            foreach (RFBone rfbone in rfBones)
            {
                // Skip excluded
                if (rfbone.excluded == true)
                    continue;

                // TODO Check if already has collider

                // Create single collider by raw size
                CreateSphereCollider (rfbone.tm, rfbone.center, rfbone.sizeRaw);
            }

            // TODO Fit colliders by cross collision check and size adjust OR mesh rayintersect checks

            // Create and apply phys mat
            PhysicsMaterial physMat = new PhysicsMaterial();
            physMat.bounciness      = 0f;
            physMat.staticFriction  = 0.9f;
            physMat.dynamicFriction = 0.9f;
            physMat.bounceCombine   = PhysicsMaterialCombine.Average;
            physMat.frictionCombine = PhysicsMaterialCombine.Average;
            foreach (var coll in colliders)
                coll.sharedMaterial = physMat;
        }

        // Create sphere collider from main bone to child
        void CreateSphereCollider(Transform tm, Transform end, float boneSize)
        {
            SphereCollider sph = tm.gameObject.AddComponent<SphereCollider>();
            sph.center = end.localPosition / 2f;
            sph.radius = sph.center.magnitude * radius * boneSize; // TODO based on mesh
            colliders.Add (sph);
        }

        // Create sphere collider from main bone to child
        void CreateSphereCollider(Transform tm, Vector3 localPos, float boneSize)
        {
            SphereCollider sph = tm.gameObject.AddComponent<SphereCollider>();
            sph.center = localPos;
            sph.radius = sph.center.magnitude * radius * boneSize; // TODO based on mesh
            colliders.Add (sph);
        }

        // Create capsule collider from main bone to child
        void CreateCapsuleCollider(Transform tm)
        {
            /* TODO optional
            CapsuleCollider capsule = rfbone.tm.AddComponent<CapsuleCollider>();
            capsule.center = (rfbone.tm.position + childTm.position) / 2f;
            capsule.radius = 0.07f; // TODO based on mesh
            */
        }

        // Destroy created colliders
        public void DestroyColliders()
        {
            if (HasColliders == false)
                return;

            foreach (var coll in colliders)
                if (coll != null)
                    DestroyImmediate (coll);
            colliders = null;
        }

        /// /////////////////////////////////////////////////////////
        /// Rigidbodies
        /// /////////////////////////////////////////////////////////

        // Create rigidbodies for each bone
        public void CreateRigidbodies()
        {
            if (HasRFBones == false)
                return;

            rigidBodies = new List<Rigidbody>();
            foreach (RFBone rfbone in rfBones)
            {
                // Skip excluded
                if (rfbone.excluded == true)
                    continue;



                // Check if already has rb and mark as not destroyable
                rfbone.rBody = rfbone.tm.GetComponent<Rigidbody>();

                // Has already
                if (rfbone.rBodyOwn == true)
                {
                    // TODO already has applied rb, but it is not own
                }

                if (rfbone.rBody != null)
                    rfbone.rBodyOwn = true;
                else
                    rfbone.rBody = rfbone.tm.gameObject.AddComponent<Rigidbody>();

                // TODO set mass by bone size relative to total mass
                rfbone.rBody.mass = 1f;
                rfbone.rBody.linearDamping = 5f;
                rigidBodies.Add (rfbone.rBody);
            }
        }

        // Destroy created rigidbodies
        public void DestroyRigidbodies()
        {
            if (HasRFBones == false)
                return;
            if (HasRbs == false)
                return;
            if (HasJoints == true)
            {
                Debug.Log ("Destroy Joints first");
                return;
            }

            // Destroy Ragdoll rigidbodies
            foreach (var bone in rfBones)
                if (bone.rBodyOwn == false)
                    if (bone.rBody != null)
                        DestroyImmediate (bone.rBody);
            rigidBodies = null;
        }

        /// /////////////////////////////////////////////////////////
        /// Joints
        /// /////////////////////////////////////////////////////////

        // Create joints for each bone
        public void CreateJoints()
        {
            if (HasRFBones == false)
                return;

            joints = new List<Joint>();
            foreach (RFBone rfbone in rfBones)
            {
                // Skip excluded
                if (rfbone.excluded == true)
                    continue;

                // Skip joint for main root
                if (rfbone.parent == null)
                    continue;

                // Check if already has joint and mar as not destroyable TODO check for other joint type
                CharacterJoint joint = rfbone.tm.GetComponent<CharacterJoint>();
                if (joint == null)
                    joint = rfbone.tm.gameObject.AddComponent<CharacterJoint>();

                // Set parent rb as connected body TODO set cached rb
                joint.connectedBody = rfbone.parent.GetComponent<Rigidbody>();

                // Get joint twist axis over bone direction
                Vector3 boneAxis = Vector3.zero;
                foreach (var child in rfbone.childTms)
                    boneAxis += child.localPosition;
                if (rfbone.dummy != null)
                    boneAxis += rfbone.dummy.localPosition;
                joint.axis = boneAxis.normalized;

                // joint.swingAxis = 

                // Twist Limit Spring
                SoftJointLimitSpring twistLimitSpring = new SoftJointLimitSpring();
                twistLimitSpring.spring = 10f;
                twistLimitSpring.damper = 1000f;
                joint.twistLimitSpring  = twistLimitSpring;

                // Low Twist Limit
                SoftJointLimit lowTwistLimit = new SoftJointLimit();
                lowTwistLimit.limit           = twist;
                lowTwistLimit.bounciness      = 0;
                lowTwistLimit.contactDistance = 0;
                joint.lowTwistLimit           = lowTwistLimit;

                // High Twist Limit
                SoftJointLimit highTwistLimit = new SoftJointLimit();
                highTwistLimit.limit           = twist;
                highTwistLimit.bounciness      = 0;
                highTwistLimit.contactDistance = 0;
                joint.highTwistLimit           = highTwistLimit;

                // Swing Limit Spring
                SoftJointLimitSpring swingLimitSpring = new SoftJointLimitSpring();
                swingLimitSpring.spring = 2f;
                swingLimitSpring.damper = 1000f;
                joint.swingLimitSpring  = swingLimitSpring;

                // Swing 1 Limit
                SoftJointLimit swing1Limit = new SoftJointLimit();
                swing1Limit.limit           = swing1;
                swing1Limit.bounciness      = 0;
                swing1Limit.contactDistance = 0;
                joint.swing1Limit           = swing1Limit;

                // Swing 2 Limit
                SoftJointLimit swing2Limit = new SoftJointLimit();
                swing2Limit.limit           = swing2;
                swing2Limit.bounciness      = 0;
                swing2Limit.contactDistance = 0;
                joint.swing2Limit           = swing2Limit;

                // Collision
                joint.enableCollision = false;

                joints.Add (joint);
            }
        }

        // Destroy created joints
        public void DestroyJoints()
        {
            if (HasJoints == false)
                return;

            foreach (var joint in joints)
                if (joint != null)
                    DestroyImmediate (joint);
            joints = null;
        }

        /// /////////////////////////////////////////////////////////
        /// Methods
        /// /////////////////////////////////////////////////////////
        
        public void RemapAnimator(Animator animatorOld, Animator animatorNew)
        {
            for (int t = 0; t < animatorOld.layerCount; ++t)
            {
                AnimatorStateInfo animatorStateInfo = animatorOld.GetCurrentAnimatorStateInfo (t);
                animatorNew.Play (animatorStateInfo.fullPathHash, t, animatorStateInfo.normalizedTime);
                Debug.Log (animatorStateInfo.fullPathHash);
                Debug.Log (animatorStateInfo.normalizedTime);
            }
        }
        
        int BoolToInt(bool state)
        {
            return state == true ? 1 : 0;
        }
        
        /// /////////////////////////////////////////////////////////
        /// Getters
        /// /////////////////////////////////////////////////////////

        public bool HasRFBones
        {
            get
            {
                if (rfBones == null)
                    return false;
                if (rfBones.Count == 0)
                    return false;
                return true;
            }
        }

        public bool HasRbs
        {
            get
            {
                if (rigidBodies == null)
                    return false;
                if (rigidBodies.Count == 0)
                    return false;
                return true;
            }
        }

        public bool HasColliders
        {
            get
            {
                if (colliders == null)
                    return false;
                if (colliders.Count == 0)
                    return false;
                return true;
            }
        }

        public bool HasJoints
        {
            get
            {
                if (joints == null)
                    return false;
                if (joints.Count == 0)
                    return false;
                return true;
            }
        }
    }
}

 /*
        
        // Skinned slice ops
        static void SkinnedMeshOps(RayfireRigid scr, Plane forcePlane)
        {
            // Get rigid of sliced skin
            RayfireRigid rigid = scr.mshDemol.engine.mainRoot.gameObject.GetComponent<RayfireRigid>();
            Debug.Log ("Skinned Mesh", rigid.gameObject);
            
            // Get ragdoll component
            RayfireRagdoll ragOrig = rigid.GetComponent<RayfireRagdoll>();
            if (ragOrig == null)
                ragOrig = rigid.gameObject.AddComponent<RayfireRagdoll>();
            
            // Get all sliced skin meshes TODO get from Rigid
            ragOrig.skins = ragOrig.GetComponentsInChildren<SkinnedMeshRenderer> (false);
            
            // Create Rag
            RFRag rag = new RFRag();
            
            // Set RFSkins and bones list
            RFRag.SetSkinBones (rag, ragOrig.skins);

            // Get sides of skins relative to slice plane TODO support multi slice sides
            RFRag.SetSkinSides (rag, forcePlane);
            
                        
                        Debug.Log ("------ SetSkinSides " + rag.sides);
                        for (int j = 0; j < rag.rfSkins.Count; j++)
                            Debug.Log (rag.rfSkins[j].sideId + " ID " + rag.rfSkins[j].id + " j "  + j + "  " + rag.rfSkins[j].skin.name, rag.rfSkins[j].skin.gameObject);
                        
             
            // Set chains
            RFRag.SetBoneChains (rag);
            
            // Create root for dead chains
            GameObject flatParent = new GameObject("FlatParent");
            flatParent.transform.position = scr.transform.position;
            
            
            
                        for (int j = 0; j < rag.chains.Count; j++)
                        {

                            if (rag.chains[j].live == true)
                                continue;

                            Debug.Log ("============ sideChain " + j);
                            Debug.Log ("============ bones: " + rag.chains[j].boneIds.Count);
                            Debug.Log ("============ parents: " + rag.chains[j].parentIds.Count);


                            Transform tm = rag.MainBone (rag.chains[j]);

                            if (tm == null)
                                Debug.Log ("main parent bone  null" );
                            else
                                Debug.Log (" +++++++++  main parent bone  " + tm, tm.gameObject);

                            for (int k = 0; k < rag.chains[j].boneIds.Count; k++)
                            {
                                int id = rag.chains[j].boneIds[k];
                                int parid = rag.chains[j].parentIds[k];

                                Debug.Log (id + " _" + rag.rfBones[id].tm.name,      rag.rfBones[id].tm.gameObject);
                                if (parid >= 0)
                                 Debug.Log (parid +" _" + rag.rfBones[parid].tm.name, rag.rfBones[parid].tm.gameObject);
                                else
                                    Debug.Log ("has no parent");
                            }
                        }

            


            // Duplicate dead chains by instancing main parent, then cut tails
            for (int i = 0; i < rag.chains.Count; i++)
            {
                Debug.Log (rag.chains[i].live);
                
                if (rag.chains[i].live == false)
                {
                    // TODO Disable colliders in dead chins
                    
                    // Get main parent chain root
                    Transform mainBone = rag.MainBone(rag.chains[i]);
                    
                    // Duplicate chain bones
                    RFChain.DuplicateChain (rag, rag.chains[i], flatParent.transform);
                }
            }

            // Reskin dead skin to duplicated dead bones
            
            
            
            
            

            
            
            Debug.Log (rag.rfSkins[10].skin);
            Debug.Log (rag.rfSkins[10].boneIds.Count);
            for (int i = 0; i < rag.rfSkins[10].boneIds.Count(); i++)
            {
                int ind = rag.rfSkins[10].boneIds[i];
                Debug.Log (rag.rfBones[ind].tm, rag.rfBones[ind].tm.gameObject);
            }
            
            // Debug
            if (rag.AllDead() == true)
                Debug.Log ("All Dead");
            if (rag.AllAlive() == true)
                Debug.Log ("All Alive");
            if (rag.HasAlive() == true)
                Debug.Log ("Has Alive");
            
            
            // Flatten dead bones
            
            // Disconnect bones child/parent by chains
            
            // Check for shared bones in chains
            
            // Shared bone duplication for all chains, collider ignoring
            
            // Disable colliders on alive duplicated bones

            
            /*
        
            // Set bone structure
            if (ragOrig.HasRFBones == false)
                ragOrig.SetBoneStructure();

            // Set colliders
            if (ragOrig.HasColliders == false)
                ragOrig.CreateColliders();

            // Set rigidbodies
            if (ragOrig.HasRbs == false)
                ragOrig.CreateRigidbodies();
            
            // Set joints
            if (ragOrig.HasJoints == false)
                ragOrig.CreateJoints();
        
            // Collect bones on two sides, shared bones has value 2
            int[] boneSides = ragOrig.GetBonesSides (ragOrig.rfBones, forcePlane);
            
            // Duplicate by halfs
            GameObject instance = Object.Instantiate (rigid.gameObject);
            
            // Get ragdoll component
            RayfireRagdoll ragInst = instance.GetComponent<RayfireRagdoll>();
            
            // Get all sliced skin meshes
            ragInst.skins = ragInst.GetComponentsInChildren<SkinnedMeshRenderer> (false);
            
            // Set bone structure
            if (ragInst.HasRFBones == false)
                ragInst.SetBoneStructure();
            
            // Set sides to bones
            for (int i = 0; i < ragOrig.rfBones.Count(); i++)
            {
                ragOrig.rfBones[i].side = boneSides[i];
                ragInst.rfBones[i].side = boneSides[i];
            }
        
            // TODO Set rootBone as not destroyable, mark with index 2
            Transform rootBoneInst = ragInst.skins[0].rootBone;
            
            // ragInst.skins[0].sharedMesh.boneWeights;
            // BoneWeight bn = new BoneWeight();

            // Destroy skins from opposite sides
            DestroyOppositeSkins (ragOrig, ragInst, skinSides);
              
            // TODO Destroy rb/collider on rootBOne
            Rigidbody rb = rootBoneInst.GetComponent<Rigidbody>();
            Object.Destroy (rb);
            
            // Destroy bones and skins from opposite sides  - ALIVE case
            for (int i = 0; i < ragOrig.rfBones.Count; i++)
            {
                if (ragOrig.rfBones[i].side == 0)
                {
                    Object.Destroy (ragOrig.rfBones[i].tm.gameObject);
                }
            }
                        
            // Destroy bones and skins from opposite sides - DEAD case
            for (int i = 0; i < ragInst.rfBones.Count; i++)
            {
                if (ragInst.rfBones[i].side == 0 || ragInst.rfBones[i].side == 2)
                {
                    // ragInst.rfBones[i].tm.parent = ragInst.transform;
                }
                
                // TODO destroy joint in top chain bone
                if (ragInst.rfBones[i].side == 2)
                {
                    if (ragInst.rfBones[i].parent == null || ragInst.rfBones[i].parent == rootBoneInst)
                    {
                        Joint joint = ragInst.rfBones[i].tm.GetComponent<Joint>();
                        Object.Destroy (joint);
                    }
                }
                
                if (ragInst.rfBones[i].side == 1)
                {
                    if (ragInst.rfBones[i].tm != rootBoneInst)
                    {
                        // Object.Destroy (ragInst.rfBones[i].tm.gameObject);
                    }
                }                
            }
                                            
            // TODO separate to dead or alive chains
            
            // TODO isolate chains, destroy the rest bones
                            
            // Check for dead/alive chains
            
            // Get animators
            Animator animInput = scr.GetComponent<Animator>();
            Animator animOrig  = rigid.GetComponent<Animator>();
            Animator animInst  = instance.GetComponent<Animator>();
            
            // Sync animators
            // ragOrig.RemapAnimator (animInput, animInst);
            
            // Destroy animator TODO if dead skinned mesh
            // Object.Destroy (animOrig);
            Object.Destroy (animInst);
            
            
        }
    
        */