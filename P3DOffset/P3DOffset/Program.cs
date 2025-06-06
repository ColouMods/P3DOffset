using System.Numerics;
using NetP3DLib.P3D;
using NetP3DLib.P3D.Chunks;

namespace P3DOffset;

static class Program {
	const float deg2Rad = MathF.PI / 180;
	
	static string inputPath = string.Empty;
	static string outputPath = string.Empty;
	static bool forceOverwrite = false;
	
	static Vector3 translation = new(0, 0, 0);
	static Vector3 rotation = new(0, 0, 0);
	static char[] order = ['z', 'y', 'x'];
	static Matrix4x4 rotMtrx;
	static Matrix4x4 transform;
	static Quaternion rotQuat;
	
	static P3DFile p3dFile = new();
	static List<string> drawablesToSkip = new();
	static List<string> lightGroupsToSkip = new();

	public static void Main(string[] args)
	{
		// Check whether arguments have been passed.
		if (args.Length == 0)
		{
			Console.WriteLine("Error: No arguments specified.");
			Console.WriteLine();
			PrintHelp();
			Environment.Exit(1);
		}
		
		// Get variable values from command line arguments.
		for (int i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "-h" or "--help":
					PrintHelp();
					Environment.Exit(0);
					break;
				case "-i" or "--input":
					inputPath = GetArgValue(args, i);
					break;
				case "-o" or "--output":
					outputPath = GetArgValue(args, i);
					break;
				case "-f" or "--force":
					forceOverwrite = true;
					break;
				case "-x":
					translation.X = ParseFloat(GetArgValue(args, i));
					break;
				case "-y":
					translation.Y = ParseFloat(GetArgValue(args, i));
					break;
				case "-z":
					translation.Z = ParseFloat(GetArgValue(args, i));
					break;
				case "-rx":
					rotation.X = LimitEulerAngle(ParseFloat(GetArgValue(args, i)));
					break;
				case "-ry":
					rotation.Y = LimitEulerAngle(ParseFloat(GetArgValue(args, i)));
					break;
				case "-rz":
					rotation.Z = LimitEulerAngle(ParseFloat(GetArgValue(args, i)));
					break;
				case "-ro" or "--order":
					var orderString = GetArgValue(args, i);
					order = orderString.ToLower().ToCharArray();
					
					// Make sure order contains only X, Y, & Z
					var orderSorted = (char[]) order.Clone();
					Array.Sort(orderSorted);
					if (orderSorted is not ['x', 'y', 'z'])
					{
						Console.WriteLine($"Error: \"{orderString}\" is not a valid rotation order.");
						Environment.Exit(3);
					}
					break;
			}
		}

		// Check input and output files have been specified.
		if (string.IsNullOrEmpty(inputPath))
		{
			Console.WriteLine("Error: No input file specified.");
			Environment.Exit(4);
		}

		if (string.IsNullOrEmpty(outputPath))
		{
			Console.WriteLine("Error: No output file specified.");
			Environment.Exit(4);
		}

		// Make sure input file exists.
		if (!File.Exists(inputPath))
		{
			Console.WriteLine("Error: Input file does not exist.");
			Environment.Exit(5);
		}
		
		// Make sure output file doesn't already exist.
		if (!forceOverwrite && File.Exists(outputPath))
		{
			Console.WriteLine($"Output file \"{outputPath}\" already exists.");
			Console.Write("Do you want to overwrite? (Y/N): ");
			var response = Console.ReadLine();
			if (response != null && response.ToLower()[0] != 'y')
			{
				Console.WriteLine("Error: Could not write output file. File already exists.");
				Environment.Exit(7);
			}
		}
		
		// Make sure that a translation/rotation is actually being applied.  
		if (translation == Vector3.Zero && rotation == Vector3.Zero)
		{
			Console.WriteLine("Error: Translation and rotation are both 0.");
			Environment.Exit(8);
		}
		
		// Convert input euler angles to rotation matrix.
		rotMtrx = GetRotationMatrix(rotation.X * deg2Rad, rotation.Y * deg2Rad, rotation.Z * deg2Rad);
		
		// Convert rotation matrix to quaternion.
		rotQuat = Quaternion.CreateFromRotationMatrix(rotMtrx);
		
		// Convert input translation to translation matrix.
		var transMtrx = Matrix4x4.CreateTranslation(translation);
		
		// Combine rotation matrix and translation matrix into transformation matrix.
		transform = rotMtrx * transMtrx;

		// Create P3DFile Object
		Console.WriteLine($"Reading file \"{inputPath}\"...");
		try
		{
			p3dFile = new P3DFile(inputPath);
		}
		catch (Exception e)
		{
			Console.WriteLine("Error: Could not read input file. " + e.Message);
			Environment.Exit(6);
		}

		// Offset Chunks
		Console.WriteLine("Applying offsets...");
		
		foreach (var chunk in p3dFile.Chunks)
		{
			switch (chunk)
			{
				// Camera (0x2200)
				case CameraChunk camera:
				{
					camera.Position = Vector3.Transform(camera.Position, transform);
					camera.Look = Vector3.Transform(camera.Look, rotMtrx);
					camera.Up = Vector3.Transform(camera.Up, rotMtrx);
					
					OffsetAnimation(camera.Name);
					break;
				}
				
				// Old Billboard Quad Group (0x17002)
				case OldBillboardQuadGroupChunk:
				{
					foreach (var billboard in chunk.GetChunksOfType<OldBillboardQuadChunk>())
					{
						billboard.Translation = Vector3.Transform(billboard.Translation, transform);
					}
				
					break;
				}
				
				// Scenegraph (0x120100)
				case ScenegraphChunk scenegraph:
				{
					foreach (var scenegraphRoot in scenegraph.GetChunksOfType<OldScenegraphRootChunk>())
					{
						foreach (var scenegraphBranch in scenegraphRoot.GetChunksOfType<OldScenegraphBranchChunk>())
						{
							OffsetScenegraphChunks(scenegraphBranch, isRoot: true, rootName: scenegraph.Name);
						}
					}
				
					break;
				}
				
				// Road (0x3000003)
				case RoadChunk:
				{
					foreach (var roadSegment in chunk.GetChunksOfType<RoadSegmentChunk>())
					{
						roadSegment.Transform *= transform;
					}
				
					break;
				}
				
				// Intersection (0x3000004)
				case IntersectionChunk intersection:
				{
					intersection.Position = Vector3.Transform(intersection.Position, transform);
					break;
				}
				
				// Locator (0x3000005)
				case LocatorChunk locator:
				{
					locator.Position = Vector3.Transform(locator.Position, transform);

					switch (locator.TypeData)
					{
						// Locator Type 3
						case LocatorChunk.Type3LocatorData type3Data:
						{
							var rot = type3Data.Rotation;
						
							// Convert rotation to forward vector.
							var forward = new Vector3(MathF.Sin(rot), 0, MathF.Cos(rot));
						
							// Apply rotation matrix to forward vector.
							forward = Vector3.Transform(forward, rotMtrx);
						
							// Extract new rotation from transformed forward vector.
							rot = float.Atan2(forward.X, forward.Z);
						
							type3Data.Rotation = LimitEulerAngle(rot, radians: true);
							break;
						}
						
						// Locator Type 7
						case LocatorChunk.Type7LocatorData type7Data:
						{
							type7Data.Right = Vector3.Transform(type7Data.Right, rotMtrx);
							type7Data.Up = Vector3.Transform(type7Data.Up, rotMtrx);
							type7Data.Front = Vector3.Transform(type7Data.Front, rotMtrx);
							break;
						}
						
						// Locator Type 8
						case LocatorChunk.Type8LocatorData type8Data:
						{
							type8Data.Right = Vector3.Transform(type8Data.Right, rotMtrx);
							type8Data.Up = Vector3.Transform(type8Data.Up, rotMtrx);
							type8Data.Front = Vector3.Transform(type8Data.Front, rotMtrx);
							break;
						}
						
						// Locator Type 12
						case LocatorChunk.Type12LocatorData type12Data:
						{
							if (type12Data.FollowPlayer == 0)
							{
								type12Data.TargetPosition = Vector3.Transform(type12Data.TargetPosition, transform);
							}

							break;
						}
					}

					foreach (var trigger in locator.GetChunksOfType<TriggerVolumeChunk>())
					{
						trigger.Matrix *= transform;
					}
				
					foreach (var matrix in locator.GetChunksOfType<LocatorMatrixChunk>())
					{
						matrix.Matrix *= transform;
					}

					foreach (var spline in locator.GetChunksOfType<SplineChunk>())
					{
						for (int i = 0; i < spline.Positions.Count; i++)
						{
							spline.Positions[i] = Vector3.Transform(spline.Positions[i], transform);
						}
					}

					break;
				}
				
				// Path (0x300000B)
				case PathChunk path:
				{
					for (int i = 0; i < path.Positions.Count; i++)
					{
						path.Positions[i] = Vector3.Transform(path.Positions[i], transform);
					}

					break;
				}
				
				// Static Entity (0x3F00000)
				case StaticEntityChunk:
				{
					foreach (var mesh in chunk.GetChunksOfType<MeshChunk>())
					{
						OffsetMeshOrSkin(mesh);
					}

					break;
				}
				
				// Static Phys (0x3F00001)
				case StaticPhysChunk:
				{
					foreach (var collisionObject in chunk.GetChunksOfType<CollisionObjectChunk>())
					{
						OffsetCollisionVolumes(collisionObject);
					}

					break;
				}
				
				// Dyna Phys (0x3F00002), Inst Stat Entity (0x3F00009), Inst Stat Phys (0x3F0000A), & Anim Dyna Phys (0x3F0000E)
				case DynaPhysChunk or InstStatEntityChunk or InstStatPhysChunk or AnimDynaPhysChunk:
				{
					foreach (var instanceList in chunk.GetChunksOfType<InstanceListChunk>())
					{
						// In Instance Lists, only the second Scenegraph Transform in the Scenegraph hierarchy is
						// actually used - everything else is ignored. Hence, these need to be handled differently
						// than regular Scenegraph chunks.
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

					break;
				}
				
				// Fence (0x3F00007)
				case FenceChunk:
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

					break;
				}
				
				// Anim Coll (0x3F00008) & Anim (0x3F0000C)
				case AnimCollChunk or AnimChunk:
				{
					foreach (var drawable in chunk.GetChunksOfType<CompositeDrawableChunk>())
					{
						OffsetDrawable(drawable, parent: chunk);
					}

					break;
				}
				
				// World Sphere (0x3F0000B)
				case WorldSphereChunk:
				{
					foreach (var drawable in chunk.GetChunksOfType<CompositeDrawableChunk>())
					{
						OffsetDrawable(drawable, hierarchyParent: chunk, parent: chunk, translate: false);
					}

					foreach (var lensFlare in chunk.GetChunksOfType<LensFlareChunk>())
					{
						foreach (var drawable in lensFlare.GetChunksOfType<CompositeDrawableChunk>())
						{
							OffsetDrawable(drawable, hierarchyParent: chunk, parent: lensFlare, translate: false);
						}
					}
					break;
				}
			}
		}

		// These chunks need to be handled after the rest of the file.
		
		// Light Group (0x2380)
		foreach (var lightGroup in p3dFile.GetChunksOfType<LightGroupChunk>())
		{
			// Light Groups are seemingly relative to the camera (or something) in Scenegraphs.
			// So if the Light Group is used by a Scenegraph then it should be skipped.
			if (lightGroupsToSkip.Contains(lightGroup.Name))
			{
				continue;
			}
			
			foreach (var lightName in lightGroup.Lights)
			{
				var light = p3dFile.GetFirstChunkOfType<LightChunk>(lightName);
				if (light == null)
				{
					continue;
				}
				
				foreach (var position in light.GetChunksOfType<LightPositionChunk>())
				{
					position.Position = Vector3.Transform(position.Position, rotMtrx);
				}

				foreach (var direction in light.GetChunksOfType<LightDirectionChunk>())
				{
					direction.Direction = Vector3.Transform(direction.Direction, rotMtrx);
				}
			}
		}
		
		// Composite Drawable (0x4512)
		foreach (var drawable in p3dFile.GetChunksOfType<CompositeDrawableChunk>())
		{
			if (drawablesToSkip.Contains(drawable.Name))
			{
				continue;
			}
			
			OffsetDrawable(drawable);
		}

		// Write output file.
		Console.WriteLine($"Writing file \"{outputPath}\"...");
		try
		{
			p3dFile.Write(outputPath);
		}
		catch (Exception e)
		{
			Console.WriteLine("Error: Could not write output file. " + e.Message);
			Environment.Exit(7);
		}
		
		Console.WriteLine("Successfully wrote file!");
		Environment.Exit(0);
	}
	
	// Display help message.
	static void PrintHelp()
	{
		Console.WriteLine("Usage: P3DOffset [options]");
		Console.WriteLine();
		Console.WriteLine("Options:");
		Console.WriteLine("    -h, --help      Display help message.");
		Console.WriteLine("    -i, --input     Set path to the input file. Required.");
		Console.WriteLine("    -o, --output    Set path to the output file. Required.");
		Console.WriteLine("    -f, --force     Force overwrite the output file.");
		Console.WriteLine("    -x              Set X position offset.");
		Console.WriteLine("    -y              Set Y position offset.");
		Console.WriteLine("    -z              Set Z position offset.");
		Console.WriteLine("    -rx             Set X rotation offset in degrees.");
		Console.WriteLine("    -ry             Set Y rotation offset in degrees.");
		Console.WriteLine("    -rz             Set Z rotation offset in degrees.");
		Console.WriteLine("    -ro, --order    Set order of rotations. Defaults to 'ZYX'");
		Console.WriteLine();
		Console.WriteLine("Example:");
		Console.WriteLine("    P3DOffset -i C:\\input\\file.p3d -o C:\\output\\file.p3d -x 100 -z 50");
		Console.WriteLine("    P3DOffset -i C:\\input\\file.p3d -o C:\\output\\file.p3d -f -ry 90");
		Console.WriteLine();
	}

	// Get command line argument with error handling.
	static string GetArgValue(string[] args, int i)
	{
		if (i + 1 == args.Length)
		{
			Console.WriteLine($"Error: No value provided for \"{args[i]}\".");
			Environment.Exit(2);
		}
		return args[i + 1];
	}
	
	// Parse float from string with error handling.
	static float ParseFloat(string input)
	{
		if (!float.TryParse(input, out var output))
		{
			Console.WriteLine($"Error: \"{input}\" is not a valid float.");
			Environment.Exit(3);
		}

		return output;
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
		var matrices = new Dictionary<char, Matrix4x4>
		{
			{
				'x', new Matrix4x4(
					1f, 0f, 0f, 0f,
					0f, MathF.Cos(x), MathF.Sin(x), 0f,
					0f, -MathF.Sin(x), MathF.Cos(x), 0f,
					0f, 0f, 0f, 1f)
			},
			{
				'y', new Matrix4x4(
					MathF.Cos(y), 0f, -MathF.Sin(y), 0f,
					0f, 1f, 0f, 0f,
					MathF.Sin(y), 0f, MathF.Cos(y), 0f,
					0f, 0f, 0f, 1f)
			},
			{
				'z', new Matrix4x4(
					MathF.Cos(z), MathF.Sin(z), 0f, 0f,
					-MathF.Sin(z), MathF.Cos(z), 0f, 0f,
					0f, 0f, 1f, 0f,
					0f, 0f, 0f, 1f)
			}
		};

		// Return matrix multiplied in order of rotation order.
		return matrices[order[0]] * matrices[order[1]] * matrices[order[2]];
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
	// Composite Drawable (0x4512)
	static void OffsetDrawable(CompositeDrawableChunk drawable, Chunk? hierarchyParent = null, Chunk? parent = null, bool translate = true)
	{
		// Find skin chunks used by the Composite Drawable.
		foreach (var skinList in drawable.GetChunksOfType<CompositeDrawableSkinListChunk>())
		{
			foreach (var drawableSkin in skinList.GetChunksOfType<CompositeDrawableSkinChunk>())
			{
				// Find & offset skin referenced by the Composite Drawable Skin.
				// If hierarchy parent is null find in file, else find in parent.
				var skin = hierarchyParent is null
					? p3dFile.GetFirstChunkOfType<SkinChunk>(drawableSkin.Name)
					: hierarchyParent.GetFirstChunkOfType<SkinChunk>(drawableSkin.Name);

				if (skin == null)
				{
					continue;
				}
				OffsetMeshOrSkin(skin, translate);
			}
		}
		
		// Find skeleton referenced by the Composite Drawable.
		var skeletonName = drawable.SkeletonName;
		var skeleton = hierarchyParent is null
			? p3dFile.GetFirstChunkOfType<SkeletonChunk>(skeletonName)
			: hierarchyParent.GetFirstChunkOfType<SkeletonChunk>(skeletonName);
		
		if (skeleton == null)
		{
			return;
		}

		// Find root joint of the skeleton and apply transform.
		var rootJoint = skeleton.GetFirstChunkOfType<SkeletonJointChunk>();
		if (rootJoint == null)
		{
			return;
		}
		
		// If translate is true root joint will be translated & rotated, otherwise it will just be rotated.
		rootJoint.RestPose *= translate ? transform : rotMtrx;
		
		OffsetAnimation(skeleton.Name, rootJoint.Name, hierarchyParent, parent, translate);
	}
	
	// Mesh (0x10000) & Skin (0x10001)
	static void OffsetMeshOrSkin(Chunk mesh, bool translate = true)
	{
		float? lowX = null, lowY = null, lowZ = null;
		float? highX = null, highY = null, highZ = null;

		foreach (var primitiveGroup in mesh.GetChunksOfType<OldPrimitiveGroupChunk>())
		{
			var positionList = primitiveGroup.GetFirstChunkOfType<PositionListChunk>();
			if (positionList == null)
			{
				continue;
			}

			for (int i = 0; i < positionList.Positions.Count; i++)
			{
				// Apply transform to each vertex position.
				var pos = positionList.Positions[i];
				// If translate is true mesh will be translated & rotated, otherwise it will just be rotated.
				var newPos = Vector3.Transform(pos, translate ? transform : rotMtrx);
				positionList.Positions[i] = newPos;

				// Find min and max X, Y and Z values.
				lowX = Math.Min(lowX ?? newPos.X, newPos.X); // If lowX is null, newPos.X is substituted in instead.
				lowY = Math.Min(lowY ?? newPos.Y, newPos.Y);
				lowZ = Math.Min(lowZ ?? newPos.Z, newPos.Z);

				highX = Math.Max(highX ?? newPos.X, newPos.X);
				highY = Math.Max(highY ?? newPos.Y, newPos.Y);
				highZ = Math.Max(highZ ?? newPos.Z, newPos.Z);
			}
			
			var normalList = primitiveGroup.GetFirstChunkOfType<NormalListChunk>();
			if (normalList == null)
			{
				continue;
			}

			for (int i = 0; i < normalList.Normals.Count; i++)
			{
				// Apply rotation matrix to each normal vector.
				var normal = normalList.Normals[i];
				var newNormal = Vector3.Transform(normal, rotMtrx);
				normalList.Normals[i] = newNormal;
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
				if (positionList == null)
				{
					continue;
				}

				foreach (var position in positionList.Positions)
				{
					var dist = Vector3.Distance(position, boundingSphere.Centre);
					maxDist = Math.Max(maxDist ?? dist, dist);
				}
			}

			boundingSphere.Radius = maxDist ?? 0;
		}

		foreach (var offsetGroup in mesh.GetChunksOfType<OldExpressionOffsetsChunk>())
		{
			foreach (var offsetList in offsetGroup.GetChunksOfType<OldOffsetListChunk>())
			{
				foreach (var offset in offsetList.Offsets)
				{
					offset.Offset = Vector3.Transform(offset.Offset, translate ? transform : rotMtrx);
				}
			}
		}
	}
	
	// Old Scenegraph Transform (0x120103) & Old Scenegraph Drawable (0x120107)
	static void OffsetScenegraphChunks(Chunk rootChunk, bool isRoot = false, string rootName = "")
	{
		foreach (var chunk in rootChunk.Children)
		{
			switch (chunk)
			{
				case OldScenegraphDrawableChunk scenegraphDrawable:
				{
					// Make sure this drawable hasn't already been offset.
					var name = scenegraphDrawable.DrawableName;
					if (drawablesToSkip.Contains(name))
					{
						break;
					}
					
					// Drawables can be either Composite Drawables or Meshes, so need to try and get both.
					var compositeDrawable = p3dFile.GetFirstChunkOfType<CompositeDrawableChunk>(name);
					var mesh = p3dFile.GetFirstChunkOfType<MeshChunk>(name);

					if (compositeDrawable != null)
					{
						drawablesToSkip.Add(compositeDrawable.Name);
						
						if (!isRoot)
						{
							break;
						}
						
						OffsetDrawable(compositeDrawable);
					}
					else if (mesh != null)
					{
						drawablesToSkip.Add(mesh.Name);
						
						if (!isRoot)
						{
							break;
						}
						
						OffsetMeshOrSkin(mesh);
					}
					
					break;
				}

				case OldScenegraphLightGroupChunk scenegraphLightGroup:
				{
					lightGroupsToSkip.Add(scenegraphLightGroup.LightGroupName);
					break;
				}
				
				case OldScenegraphTransformChunk scenegraphTransform:
				{
					if (isRoot)
					{
						scenegraphTransform.Transform *= transform;
						OffsetAnimation(rootName, scenegraphTransform.Name);
					}
					
					OffsetScenegraphChunks(chunk);
					break;
				}

				case OldScenegraphVisibilityChunk:
				{
					OffsetScenegraphChunks(chunk, isRoot, rootName);
					break;
				}
			}
		}
	}
	
	// Animation (0x121000)
	static void OffsetAnimation(string hierarchyName, string? rootJointName = null, Chunk? hierarchyParent = null, Chunk? parent = null, bool translate = true)
	{
		// Find all Old Frame Controllers in parent chunk. If parent is null, find in file instead.
		var controllers = parent is null
			? p3dFile.GetChunksOfType<OldFrameControllerChunk>()
			: parent.GetChunksOfType<OldFrameControllerChunk>();
		
		foreach (var controller in controllers)
		{
			// Check if frame controller references hierarchy name.
			if (controller.HierarchyName != hierarchyName)
			{
				continue;
			}

			// Find animation chunk referenced by the frame controller.
			// If hierarchy parent is null find in file, else find in hierarchy parent.
			var animation = hierarchyParent is null
				? p3dFile.GetFirstChunkOfType<AnimationChunk>(controller.AnimationName)
				: hierarchyParent.GetFirstChunkOfType<AnimationChunk>(controller.AnimationName);

			if (animation == null)
			{
				continue;
			}
			
			var deleteSize = false;

			foreach (var groupList in animation.GetChunksOfType<AnimationGroupListChunk>())
			{
				foreach (var group in groupList.GetChunksOfType<AnimationGroupChunk>())
				{
					// If a root joint is specified, only affect the group that matches the root joint.
					if (rootJointName != null && group.Name != rootJointName)
					{
						continue;
					}
					
					// Convert 1D and 2D vector channels to 3D vectors.
					for (var i = 0; i < group.Children.Count; i++)
					{
						switch (group.Children[i])
						{
							case Vector1DOFChannelChunk vector1D:
							{
								if (vector1D.Param is not ("TRAN" or "LOOK" or "UP"))
								{
									continue;
								}
								
								// Get 3D vectors from 1D channel.
								var vectorList = vector1D.GetValues();

								// Create new 3D vector channel.
								var vector3D = new Vector3DOFChannelChunk(vector1D.Version, vector1D.Param, vector1D.Frames, vectorList);
								vector3D.Children.AddRange(vector1D.Children);

								// Replace 1D channel with 3D channel.
								group.Children[i] = vector3D;

								deleteSize = true;
								break;
							}
							
							case Vector2DOFChannelChunk vector2D:
							{
								if (vector2D.Param is not ("TRAN" or "LOOK" or "UP"))
								{
									continue;
								}
								
								// Get 3D vectors from 2D channel & apply transform.
								var vectorList = vector2D.GetValues();

								// Create new 3D vector channel.
								var vector3D = new Vector3DOFChannelChunk(vector2D.Version, vector2D.Param, vector2D.Frames, vectorList);
								vector3D.Children.AddRange(vector2D.Children);

								// Replace 2D channel with 3D channel.
								group.Children[i] = vector3D;
								
								deleteSize = true;
								break;
							}
						}
					}
					
					// Find 3D vector channels and apply transform.
					foreach (var vector in group.GetChunksOfType<Vector3DOFChannelChunk>())
					{
						if (vector.Param is not ("TRAN" or "LOOK" or "UP"))
						{
							continue;
						}

						for (int i = 0; i < vector.Values.Count; i++)
						{
							if (translate && vector.Param == "TRAN")
							{
								vector.Values[i] = Vector3.Transform(vector.Values[i], transform);
							}
							else
							{
								vector.Values[i] = Vector3.Transform(vector.Values[i], rotMtrx);
							}
						}
					}

					// Find quaternion channels and apply rotation.
					foreach (var quaternion in group.GetChunksOfType<QuaternionChannelChunk>())
					{
						if (quaternion.Param is not "ROT")
						{
							continue;
						}

						for (int i = 0; i < quaternion.Values.Count; i++)
						{
							quaternion.Values[i] = rotQuat * quaternion.Values[i];
						}
					}

					// Find compressed quaternion channels and apply rotation.
					foreach (var quaternion in group.GetChunksOfType<CompressedQuaternionChannelChunk>())
					{
						if (quaternion.Param is not "ROT")
						{
							continue;
						}

						for (int i = 0; i < quaternion.Values.Count; i++)
						{
							quaternion.Values[i] = rotQuat * quaternion.Values[i];
						}
					}
				}
			}

			// If you change the size of any animation channels, you need to also delete the Animation Size chunk.
			// Otherwise, the game gets very unhappy (aka it crashes randomly).
			if (deleteSize)
			{
				AnimationSizeChunk size;
				while ((size = animation.GetFirstChunkOfType<AnimationSizeChunk>()) != null)
				{
					animation.Children.Remove(size);
				}
			}
		}
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
			
				if (vectors.Length < 2)
				{
					continue;
				}
				
				// Apply transform to centre vector.
				vectors[0].Vector = Vector3.Transform(vectors[0].Vector, transform);

				// Apply rotation matrix to direction vectors.
				for (int i = 1; i < vectors.Length; i++)
				{
					vectors[i].Vector = Vector3.Transform(vectors[i].Vector, rotMtrx);
				}
			}

			foreach (var collisionCylinder in collisionVolume.GetChunksOfType<CollisionCylinderChunk>())
			{
				var vectors = collisionCylinder.GetChunksOfType<CollisionVectorChunk>();
				
				if (vectors.Length < 2)
				{
					continue;
				}
				
				// Apply transform to centre vector.
				vectors[0].Vector = Vector3.Transform(vectors[0].Vector, transform);
				
				// Apply rotation matrix to direction vector.
				vectors[1].Vector = Vector3.Transform(vectors[1].Vector, rotMtrx);
			}

			foreach (var collisionSphere in collisionVolume.GetChunksOfType<CollisionSphereChunk>())
			{
				var vectorCentre = collisionSphere.GetFirstChunkOfType<CollisionVectorChunk>();
			
				if (vectorCentre == null)
				{
					continue;
				}
				
				vectorCentre.Vector = Vector3.Transform(vectorCentre.Vector, transform);
			}
		}
	}
}