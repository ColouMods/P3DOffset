using System.Numerics;
using NetP3DLib.P3D;
using NetP3DLib.P3D.Chunks;

namespace P3DOffset;

static class Program {
	// Set constants.
	const float deg2Rad = MathF.PI / 180;

	// Initialise static variables.
	static Vector3 translation = new Vector3(0, 0, 0);
	static Vector3 rotation = new Vector3(0, 0, 0);
	static Matrix4x4 transform;
	static Quaternion rotQuat;

	public static void Main(string[] args)
	{
		// Initialise variables.
		var inputPath = string.Empty;
		var outputPath = string.Empty;
		var forceOverwrite = false;
		
		// Get variable values from command line arguments.
		for (int i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "-i":
					inputPath = GetArgValue(args, i);
					break;
				case "-o":
					outputPath = GetArgValue(args, i);
					break;
				case "-f":
					forceOverwrite = true;
					break;
				case "-x":
					translation.X = float.Parse(GetArgValue(args, i));
					break;
				case "-y":
					translation.Y = float.Parse(GetArgValue(args, i));
					break;
				case "-z":
					translation.Z = float.Parse(GetArgValue(args, i));
					break;
				case "-rx":
					rotation.X = LimitEulerAngle(float.Parse(GetArgValue(args, i)));
					break;
				case "-ry":
					rotation.Y = LimitEulerAngle(float.Parse(GetArgValue(args, i)));
					break;
				case "-rz":
					rotation.Z = LimitEulerAngle(float.Parse(GetArgValue(args, i)));
					break;
			}
		}
		
		// Convert input euler angles to rotation matrix.
		var rotMtrx = GetRotationMatrix(rotation.X * deg2Rad, rotation.Y * deg2Rad, rotation.Z * deg2Rad);
		
		// Convert rotation matrix to quaternion.
		rotQuat = Quaternion.CreateFromRotationMatrix(rotMtrx);
		
		// Convert input translation to translation matrix.
		var transMtrx = Matrix4x4.CreateTranslation(translation);
		
		// Combine rotation matrix and translation matrix into transformation matrix.
		transform = rotMtrx * transMtrx;

		// Check input and output files have been specified.
		if (string.IsNullOrEmpty(inputPath))
		{
			Console.WriteLine("Error: No input file specified.");
			Environment.Exit(2);
		}

		if (string.IsNullOrEmpty(outputPath))
		{
			Console.WriteLine("Error: No output file specified.");
			Environment.Exit(2);
		}

		// Check input file exists.
		if (File.Exists(inputPath) == false)
		{
			Console.WriteLine("Error: Input file does not exist.");
			Environment.Exit(3);
		}

		// Create P3DFile Object
		Console.WriteLine($"Reading {inputPath}...");
		P3DFile p3dFile = new(inputPath);

		// Offset Chunks
		foreach (var chunk in p3dFile.Chunks)
		{
			// Old Billboard Quad Group (0x17002)
			if (chunk is OldBillboardQuadGroupChunk)
			{
				foreach (var billboard in chunk.GetChunksOfType<OldBillboardQuadChunk>())
				{
					billboard.Translation = Vector3.Transform(billboard.Translation, transform);
				}
				continue;
			}
			
			// Locator (0x3000005)
			if (chunk is LocatorChunk locatorChunk)
			{
				locatorChunk.Position = Vector3.Transform(locatorChunk.Position, transform);

				// Locator Type 3
				if (locatorChunk.TypeData is LocatorChunk.Type3LocatorData type3Data)
				{
					var rot = type3Data.Rotation;
					rot += (rotation.Y * deg2Rad);
					type3Data.Rotation = LimitEulerAngle(rot, radians: true);
				}
				
				// Locator Type 7
				if (locatorChunk.TypeData is LocatorChunk.Type7LocatorData type7Data)
				{
					var vectors = OffsetLocatorMatrixList(type7Data.Right, type7Data.Up, type7Data.Front);

					type7Data.Right = vectors.right;
					type7Data.Up = vectors.up;
					type7Data.Front = vectors.front;
				}
				
				// Locator Type 8
				if (locatorChunk.TypeData is LocatorChunk.Type8LocatorData type8Data)
				{
					var vectors = OffsetLocatorMatrixList(type8Data.Right, type8Data.Up, type8Data.Front);

					type8Data.Right = vectors.right;
					type8Data.Up = vectors.up;
					type8Data.Front = vectors.front;
				}
				
				// Locator Type 12
				if (locatorChunk.TypeData is LocatorChunk.Type12LocatorData type12Data)
				{
					if (type12Data.FollowPlayer == 0)
					{
						type12Data.TargetPosition = Vector3.Transform(type12Data.TargetPosition, transform);
					}
				}

				foreach (var trigger in locatorChunk.GetChunksOfType<TriggerVolumeChunk>())
				{
					trigger.Matrix *= transform;
				}
				
				foreach (var matrix in locatorChunk.GetChunksOfType<LocatorMatrixChunk>())
				{
					matrix.Matrix *= transform;
				}

				foreach (var spline in locatorChunk.GetChunksOfType<SplineChunk>())
				{
					for (int i = 0; i < spline.Positions.Count; i++)
					{
						spline.Positions[i] = Vector3.Transform(spline.Positions[i], transform);
					}
				}

				continue;
			}
			
			// Path (0x300000B)
			if (chunk is PathChunk pathChunk)
			{
				for (int i = 0; i < pathChunk.Positions.Count; i++)
				{
					pathChunk.Positions[i] = Vector3.Transform(pathChunk.Positions[i], transform);
				}
			}
			
			// Static Entity (0x3F00000)
			if (chunk is StaticEntityChunk)
			{
				foreach (var mesh in chunk.GetChunksOfType<MeshChunk>())
				{
					float? lowX = null, lowY = null, lowZ = null;
					float? highX = null, highY = null, highZ = null;

					foreach (var primitiveGroup in mesh.GetChunksOfType<OldPrimitiveGroupChunk>())
					{
						var positionList = primitiveGroup.GetFirstChunkOfType<PositionListChunk>();
						if (positionList == null) continue;

						for (int i = 0; i < positionList.Positions.Count; i++)
						{
							// Apply transform to each vertex position.
							var pos = positionList.Positions[i];
							var newPos = Vector3.Transform(pos, transform);
							positionList.Positions[i] = newPos;

							// Find min and max X, Y and Z values.
							lowX = Math.Min(lowX ?? newPos.X,
								newPos.X); // If lowX is null, newPos.X is substituted in instead.
							lowY = Math.Min(lowY ?? newPos.Y, newPos.Y);
							lowZ = Math.Min(lowZ ?? newPos.Z, newPos.Z);

							highX = Math.Max(highX ?? newPos.X, newPos.X);
							highY = Math.Max(highY ?? newPos.Y, newPos.Y);
							highZ = Math.Max(highZ ?? newPos.Z, newPos.Z);
						}
					}

					// Set Bounding Box values.
					var bbLow = new Vector3(lowX ?? 0, lowY ?? 0, lowZ ?? 0);
					var bbHigh = new Vector3(highX ?? 0, highY ?? 0, highZ ?? 0);

					var boundingBox = mesh.GetFirstChunkOfType<BoundingBoxChunk>();

					if (boundingBox != null)
					{
						boundingBox.Low = bbLow;
						boundingBox.High = bbHigh;
					}

					// Set Bounding Sphere values.
					var boundingSphere = mesh.GetFirstChunkOfType<BoundingSphereChunk>();

					if (boundingSphere != null)
					{
						boundingSphere.Centre = new Vector3(
							(bbLow.X + bbHigh.X) / 2,
							(bbLow.Y + bbHigh.Y) / 2,
							(bbLow.Z + bbHigh.Z) / 2);

						// Find furthest away vertex from the bounding sphere centre.
						float? maxDist = null;
						foreach (var primitiveGroup in mesh.GetChunksOfType<OldPrimitiveGroupChunk>())
						{
							var positionList = primitiveGroup.GetFirstChunkOfType<PositionListChunk>();
							if (positionList == null) continue;

							foreach (var position in positionList.Positions)
							{
								var dist = Vector3.Distance(position, boundingSphere.Centre);
								maxDist = Math.Max(maxDist ?? dist, dist);
							}
						}

						boundingSphere.Radius = maxDist ?? 0;
					}
				}

				continue;
			}

			// Static Phys (0x3F00001)
			if (chunk is StaticPhysChunk)
			{
				foreach (var collisionObject in chunk.GetChunksOfType<CollisionObjectChunk>())
				{
					OffsetCollisionVolumes(collisionObject);
				}

				continue;
			}

			// Dyna Phys (0x3F00002), Inst Stat Entity (0x3F00009), Inst Stat Phys (0x3F0000A), & Anim Dyna Phys (0x3F0000E)
			if (chunk is DynaPhysChunk or InstStatEntityChunk or InstStatPhysChunk or AnimDynaPhysChunk)
			{
				foreach (var instanceList in chunk.GetChunksOfType<InstanceListChunk>())
				{
					foreach (var scenegraph in instanceList.GetChunksOfType<ScenegraphChunk>())
					{
						foreach (var scenegraphRoot in scenegraph.GetChunksOfType<OldScenegraphRootChunk>())
						{
							foreach (var scenegraphBranch in scenegraphRoot.GetChunksOfType<OldScenegraphBranchChunk>())
							{
								foreach (var scenegraphTransform in scenegraphBranch.GetChunksOfType<OldScenegraphTransformChunk>())
								{
									foreach (var subScenegraphTransform in scenegraphTransform.GetChunksOfType<OldScenegraphTransformChunk>())
									{
										subScenegraphTransform.Transform *= transform;
									}
								}
							}
						}
					}
				}

				continue;
			}
			
			// Fence (0x3F00007)
			if (chunk is FenceChunk)
			{
				foreach (var wall in chunk.GetChunksOfType<WallChunk>())
				{
					// Calculate normal from original start/end positions.
					var calcNormal = CalculateFenceNormal(wall.Start, wall.End, false);

					// Check whether it matches the original normal, if not assume it's inverted.
					float tolerance = 0.001f;
					bool inverted = Math.Abs(wall.Normal.X - calcNormal.X) > tolerance;
					
					// Calculate new start/end positions.
					var newStart = Vector3.Transform(wall.Start, transform);
					wall.Start = new Vector3(newStart.X, 0, newStart.Z);
					
					var newEnd = Vector3.Transform(wall.End, transform);
					wall.End = new Vector3(newEnd.X, 0, newEnd.Z);

					// Calculate normal from new start/end positions.
					wall.Normal = CalculateFenceNormal(wall.Start, wall.End, inverted);
				}
			}
			
			// Anim Coll (0x3F00008) & Anim (0x3F0000C)
			if (chunk is AnimCollChunk or AnimChunk)
			{
				var drawable = chunk.GetFirstChunkOfType<CompositeDrawableChunk>();
				if (drawable == null) break;

				// Find skeleton referenced by the Composite Drawable.
				var skeletonName = drawable.SkeletonName;

				var skeleton = p3dFile.GetFirstChunkOfType<SkeletonChunk>(skeletonName);
				if (skeleton == null) break;

				// Find root joint of the skeleton.
				var rootJoint = skeleton.GetFirstChunkOfType<SkeletonJointChunk>();
				if (rootJoint == null) break;

				// Apply transform to root joint.
				rootJoint.RestPose *= transform;

				var controller = chunk.GetFirstChunkOfType<MultiControllerChunk>();

				if (controller == null) break;
				// Find animation names referenced by the Multi Controller
				var controllerTrack = controller.GetFirstChunkOfType<MultiControllerTracksChunk>();
				if (controllerTrack == null) continue;

				var tracks = controllerTrack.Tracks;

				foreach (var track in tracks)
				{
					var animationName = track.Name;

					// Find animation chunk that matches the referenced name.
					var animation = p3dFile.GetFirstChunkOfType<AnimationChunk>(animationName);
					if (animation == null) continue;

					foreach (var groupList in animation.GetChunksOfType<AnimationGroupListChunk>())
					{
						// Find animation group that corresponds to the skeleton root chunk.
						var rootGroup = groupList.GetFirstChunkOfType<AnimationGroupChunk>(rootJoint.Name);
						if (rootGroup == null) continue;

						// Find TRAN channels and apply transform.
						foreach (var vectors in rootGroup.GetChunksOfType<Vector3DOFChannelChunk>())
						{
							if (vectors.Param != "TRAN") continue;

							for (int i = 0; i < vectors.Values.Count; i++)
							{
								vectors.Values[i] = Vector3.Transform(vectors.Values[i], transform);
							}
						}

						// Find ROT channels and apply rotation.
						foreach (var quaternions in rootGroup.GetChunksOfType<QuaternionChannelChunk>())
						{
							if (quaternions.Param != "ROT") continue;

							for (int i = 0; i < quaternions.Values.Count; i++)
							{
								quaternions.Values[i] = rotQuat * quaternions.Values[i];
							}
						}
					}
				}

				continue;
			}
		}

		// Check if output file already exists.
		if (!forceOverwrite && File.Exists(outputPath))
		{
			Console.WriteLine($"Output file \"{outputPath}\" already exists.");
			Console.WriteLine("Do you want to overwrite? (Y/N): ");
			var response = Console.ReadLine();
			if (response != null && response.ToLower()[0] != 'y')
			{
				Console.WriteLine("Error: File could not be written.");
				Environment.Exit(4);
			}
		}

		// Write output file.
		p3dFile.Write(outputPath);
		Console.WriteLine("Successfully wrote file!");
		Environment.Exit(0);
	}

	// Get command line argument with error handling.
	static string GetArgValue(string[] args, int i)
	{
		if (i == args.Length)
		{
			Console.WriteLine($"Error: No value provided for {args[i]}.");
			Environment.Exit(1);
		}
		return args[i + 1];
	}

	// Limit the inputted euler angle between -179 and 180 degrees.
	static float LimitEulerAngle(float angle, bool radians = false)
	{
		if (radians)
		{
			return (angle - MathF.PI) % (MathF.PI * -2) + MathF.PI;
		}

		return (angle - 180) % -360 + 180;
	}
	
	// Calculate rotation matrix based on euler angles.
	static Matrix4x4 GetRotationMatrix(float x, float y, float z)
	{
		var xMtrx = new Matrix4x4(
			1f, 0f, 0f, 0f,
			0f, MathF.Cos(x), MathF.Sin(x), 0f,
			0f, -MathF.Sin(x), MathF.Cos(x), 0f,
			0f, 0f, 0f, 1f
		);
    
		var yMtrx = new Matrix4x4(
			MathF.Cos(y), 0f, -MathF.Sin(y), 0f,
			0f, 1f, 0f, 0f,
			MathF.Sin(y), 0f, MathF.Cos(y), 0f,
			0f, 0f, 0f, 1f
		);
    
		var zMtrx = new Matrix4x4(
			MathF.Cos(z), MathF.Sin(z), 0f, 0f,
			-MathF.Sin(z), MathF.Cos(z), 0f, 0f,
			0f, 0f, 1f, 0f,
			0f, 0f, 0f, 1f
		);
		
		return zMtrx * yMtrx * xMtrx;
	}
	
	// Calculate fence normal from start and end point.
	static Vector3 CalculateFenceNormal(Vector3 v1, Vector3 v2, bool inverted)
	{
		// Get 2D direction vector from X and Z values.
		Vector2 dir = new Vector2(v2.X - v1.X, v2.Z - v1.Z);

		// Get perpendicular direction vector.
		Vector2 normal2D = new Vector2(-dir.Y, dir.X);

		// Normalize perpendicular vector.
		normal2D = Vector2.Normalize(normal2D);

		// Return as 3D vector. If it's inverted then invert the X & Z values.
		if (inverted)
		{
			return new Vector3(-normal2D.X, 0, -normal2D.Y);
		}
		
		return new Vector3(normal2D.X, 0, normal2D.Y);
	}

	// Offset Chunk Methods //
	// Locator (0x3000005) Type 7 & 8
	static (Vector3 right, Vector3 up, Vector3 front) OffsetLocatorMatrixList(Vector3 right, Vector3 up, Vector3 front)
	{
		var matrix = new Matrix4x4(
			right.X, right.Y, right.Z, 0,
			up.X, up.Y, up.Z, 0,
			front.X, front.Y, front.Z, 0,
			0, 0, 0, 1
		);

		matrix *= transform;
		
		var newRight = new Vector3(matrix.M11, matrix.M12, matrix.M13);
		var newUp = new Vector3(matrix.M21, matrix.M22, matrix.M23);
		var newFront = new Vector3(matrix.M31, matrix.M32, matrix.M33);

		return (newRight, newUp, newFront);
	}
	
	// Collision Volume (0x7010001)
	static void OffsetCollisionVolumes(Chunk chunk)
	{
		foreach (var collisionVolume in chunk.GetChunksOfType<CollisionVolumeChunk>())
		{
			OffsetCollisionVolumes(collisionVolume);

			foreach (var collisionBox in collisionVolume.GetChunksOfType<CollisionOrientedBoundingBoxChunk>())
			{
				var vectors = collisionBox.GetChunksOfType<CollisionVectorChunk>();
				if (vectors.Length != 4) continue;
				
				// Construct matrix from vectors.
				var boxRot = new Matrix4x4(vectors[1].Vector.X, vectors[1].Vector.Y, vectors[1].Vector.Z, 0,
					vectors[2].Vector.X, vectors[2].Vector.Y, vectors[2].Vector.Z, 0,
					vectors[3].Vector.X, vectors[3].Vector.Y, vectors[3].Vector.Z, 0,
					vectors[0].Vector.X, vectors[0].Vector.Y, vectors[0].Vector.Z, 1);
					
				// Apply transform to matrix.
				var newBoxRot = boxRot * transform;
					
				// Convert matrix back to vectors.
				vectors[0].Vector = new Vector3(newBoxRot.M41, newBoxRot.M42, newBoxRot.M43);
				vectors[1].Vector = new Vector3(newBoxRot.M11, newBoxRot.M12, newBoxRot.M13);
				vectors[2].Vector = new Vector3(newBoxRot.M21, newBoxRot.M22, newBoxRot.M23);
				vectors[3].Vector = new Vector3(newBoxRot.M31, newBoxRot.M32, newBoxRot.M33);
			}

			foreach (var collisionCylinder in collisionVolume.GetChunksOfType<CollisionCylinderChunk>())
			{
				var vectors = collisionCylinder.GetChunksOfType<CollisionVectorChunk>();
				if (vectors.Length != 2) continue;
				
				// Apply transform to centre vector.
				vectors[0].Vector = Vector3.Transform(vectors[0].Vector, transform);

				// Apply transform to direction vector.
				var rot = vectors[1].Vector;
				// Collision Cylinders don't store the entire rotation matrix, they store only the centre column as a vector.
				// As such, applying the transform isn't as simple as multiplying matrices.
				// But, by complete accident, I stumbled upon a formula that seems to work? (though I don't understand why)
				vectors[1].Vector = new Vector3(
					transform.M11 * rot.X + transform.M21 * rot.Y + transform.M31 * rot.Z,
					transform.M12 * rot.X + transform.M22 * rot.Y + transform.M32 * rot.Z,
					transform.M13 * rot.X + transform.M23 * rot.Y + transform.M33 * rot.Z
				);
			}

			foreach (var collisionSphere in collisionVolume.GetChunksOfType<CollisionSphereChunk>())
			{
				var vectorCentre = collisionSphere.GetFirstChunkOfType<CollisionVectorChunk>();
				if (vectorCentre == null) continue;
				
				vectorCentre.Vector = Vector3.Transform(vectorCentre.Vector, transform);
			}
		}
	}
}