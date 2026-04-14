using UnityEngine;

namespace WalaPaNameHehe
{
    public class DroneProjectile : MonoBehaviour
    {
        [SerializeField] private float hitSweepRadius = 0.08f;
        [SerializeField] private float resourceSnapRadius = 0.45f;
        [SerializeField] private bool useMeshAccurateResourceHit = true;

        private WeaponDrone ownerWeaponDrone;
        private Transform ownerRoot;
        private Transform ignoredRoot;
        private bool shouldReportHit;
        private bool hasImpacted;
        private Vector3 lastPosition;
        private bool hasLastPosition;
        private static MeshCollider meshRaycastCollider;
        private static Mesh bakedSkinnedMesh;

        public void Initialize(WeaponDrone owner, bool reportHit, Transform additionalIgnoredRoot = null)
        {
            ownerWeaponDrone = owner;
            ownerRoot = owner != null ? owner.transform.root : null;
            ignoredRoot = additionalIgnoredRoot;
            shouldReportHit = reportHit;
            hasImpacted = false;
            lastPosition = transform.position;
            hasLastPosition = true;
        }

        private void FixedUpdate()
        {
            if (hasImpacted)
            {
                return;
            }

            Vector3 currentPosition = transform.position;
            if (!hasLastPosition)
            {
                lastPosition = currentPosition;
                hasLastPosition = true;
                return;
            }

            Vector3 delta = currentPosition - lastPosition;
            float distance = delta.magnitude;
            if (distance <= 0.0001f)
            {
                lastPosition = currentPosition;
                return;
            }

            Vector3 direction = delta / distance;
            float radius = Mathf.Max(0.001f, hitSweepRadius);

            if (useMeshAccurateResourceHit && TryFindMeshHitResource(lastPosition, currentPosition, out ExtractableResource meshHitResource))
            {
                hasImpacted = true;
                if (shouldReportHit && ownerWeaponDrone != null && meshHitResource != null)
                {
                    ownerWeaponDrone.NotifyProjectileHitResource(meshHitResource);
                }

                Destroy(gameObject);
                return;
            }

            if (Physics.SphereCast(lastPosition, radius, direction, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Collide))
            {
                HandleHit(hit.collider);
            }

            lastPosition = currentPosition;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.collider == null)
            {
                return;
            }

            Vector3 impactPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : collision.collider.ClosestPoint(transform.position);
            HandleHit(collision.collider, impactPoint);
        }

        private void OnTriggerEnter(Collider other)
        {
            Vector3 impactPoint = other != null
                ? other.ClosestPoint(transform.position)
                : transform.position;
            HandleHit(other, impactPoint);
        }

        private void HandleHit(Collider hitCollider)
        {
            Vector3 impactPoint = hitCollider != null
                ? hitCollider.ClosestPoint(transform.position)
                : transform.position;
            HandleHit(hitCollider, impactPoint);
        }

        private void HandleHit(Collider hitCollider, Vector3 impactPoint)
        {
            if (hasImpacted || hitCollider == null)
            {
                return;
            }

            if (ShouldIgnoreCollider(hitCollider))
            {
                return;
            }

            ExtractableResource resource = hitCollider.GetComponentInParent<ExtractableResource>();
            if (resource == null)
            {
                resource = FindNearbyResource(impactPoint);
            }

            if (resource != null && useMeshAccurateResourceHit)
            {
                Vector3 segmentStart = hasLastPosition ? lastPosition : transform.position;
                Vector3 segmentEnd = transform.position;
                if (!IsMeshAccurateHit(resource, segmentStart, segmentEnd))
                {
                    return;
                }
            }

            // Ignore non-resource trigger volumes so they don't eat projectiles.
            if (hitCollider.isTrigger && resource == null)
            {
                return;
            }

            hasImpacted = true;
            if (shouldReportHit && ownerWeaponDrone != null && resource != null)
            {
                ownerWeaponDrone.NotifyProjectileHitResource(resource);
            }

            Destroy(gameObject);
        }

        private bool ShouldIgnoreCollider(Collider hitCollider)
        {
            if (hitCollider == null)
            {
                return true;
            }

            Transform hitRoot = hitCollider.transform.root;
            if (hitRoot == transform.root)
            {
                return true;
            }

            if (ownerRoot != null && hitRoot == ownerRoot)
            {
                return true;
            }

            if (ignoredRoot != null && hitRoot == ignoredRoot)
            {
                return true;
            }

            return false;
        }

        private ExtractableResource FindNearbyResource(Vector3 point)
        {
            float radius = Mathf.Max(0.01f, resourceSnapRadius);
            Collider[] nearby = Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < nearby.Length; i++)
            {
                Collider c = nearby[i];
                if (c == null)
                {
                    continue;
                }

                ExtractableResource resource = c.GetComponentInParent<ExtractableResource>();
                if (resource != null)
                {
                    return resource;
                }
            }

            return null;
        }

        private bool IsMeshAccurateHit(ExtractableResource resource, Vector3 segmentStart, Vector3 segmentEnd)
        {
            if (resource == null)
            {
                return false;
            }

            Vector3 delta = segmentEnd - segmentStart;
            float distance = delta.magnitude;
            if (distance <= 0.0001f)
            {
                return false;
            }

            Ray ray = new Ray(segmentStart, delta / distance);
            float maxDistance = distance + Mathf.Max(0.001f, hitSweepRadius);

            SkinnedMeshRenderer[] skinnedRenderers = resource.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                SkinnedMeshRenderer skinned = skinnedRenderers[i];
                if (skinned == null || !skinned.enabled)
                {
                    continue;
                }

                if (TryRaycastSkinnedRenderer(skinned, ray, maxDistance))
                {
                    return true;
                }
            }

            MeshFilter[] meshFilters = resource.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter filter = meshFilters[i];
                if (filter == null)
                {
                    continue;
                }

                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                Mesh shared = filter.sharedMesh;
                if (shared == null)
                {
                    continue;
                }

                if (TryRaycastMesh(filter.transform, shared, ray, maxDistance))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFindMeshHitResource(Vector3 segmentStart, Vector3 segmentEnd, out ExtractableResource resourceHit)
        {
            resourceHit = null;
            ExtractableResource[] allResources = FindObjectsByType<ExtractableResource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < allResources.Length; i++)
            {
                ExtractableResource resource = allResources[i];
                if (resource == null)
                {
                    continue;
                }

                Transform resourceRoot = resource.transform.root;
                if (resourceRoot == transform.root || resourceRoot == ownerRoot || resourceRoot == ignoredRoot)
                {
                    continue;
                }

                if (!IsMeshAccurateHit(resource, segmentStart, segmentEnd))
                {
                    continue;
                }

                resourceHit = resource;
                return true;
            }

            return false;
        }

        private bool TryRaycastSkinnedRenderer(SkinnedMeshRenderer skinned, Ray ray, float maxDistance)
        {
            if (skinned == null)
            {
                return false;
            }

            if (bakedSkinnedMesh == null)
            {
                bakedSkinnedMesh = new Mesh
                {
                    name = "DroneProjectile_BakedSkinnedMesh"
                };
            }
            else
            {
                bakedSkinnedMesh.Clear();
            }

            skinned.BakeMesh(bakedSkinnedMesh, true);
            return TryRaycastMesh(skinned.transform, bakedSkinnedMesh, ray, maxDistance);
        }

        private bool TryRaycastMesh(Transform meshTransform, Mesh mesh, Ray ray, float maxDistance)
        {
            if (meshTransform == null || mesh == null)
            {
                return false;
            }

            MeshCollider collider = GetOrCreateMeshRaycastCollider();
            if (collider == null)
            {
                return false;
            }

            Transform colliderTransform = collider.transform;
            colliderTransform.SetPositionAndRotation(meshTransform.position, meshTransform.rotation);
            colliderTransform.localScale = meshTransform.lossyScale;

            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            return collider.Raycast(ray, out _, maxDistance);
        }

        private static MeshCollider GetOrCreateMeshRaycastCollider()
        {
            if (meshRaycastCollider != null)
            {
                return meshRaycastCollider;
            }

            GameObject temp = new GameObject("DroneProjectile_MeshRaycastCollider")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            meshRaycastCollider = temp.AddComponent<MeshCollider>();
            meshRaycastCollider.convex = false;
            return meshRaycastCollider;
        }
    }
}
