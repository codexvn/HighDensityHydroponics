﻿using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace HighDensityHydro
{
	public class Building_HighDensityHydro : Building_PlantGrower, IPlantToGrowSettable
	{
		// for graphics drawing
		private static readonly Dictionary<ThingDef, Graphic> plantGraphicCache = new Dictionary<ThingDef, Graphic>();
		private Dictionary<IntVec3, Matrix4x4> drawMatrixCache = new Dictionary<IntVec3, Matrix4x4>();
		
		// Token: 0x17000001 RID: 1
		// (get) Token: 0x06000001 RID: 1
		IEnumerable<IntVec3> IPlantToGrowSettable.Cells
		{
			get
			{
				return this.OccupiedRect().Cells;
			}
		}

		// Token: 0x06000002 RID: 2
		public override void TickRare()
		{
			//Log.Message($"[HDH] Tick: storedPlants={storedPlants}, growth={growth}, stage={bayStage}");
			
			bool poweredNow = this.PowerComp == null || this.PowerComp.PowerOn;
			if (HDH_Mod.settings.killPlantsOnNoPower)
			{
				if (this.wasPoweredLastTick && !poweredNow)
				{
					this.KillAllPlantsAndReset();
				}
			}
			
			this.wasPoweredLastTick = poweredNow;
			switch (this.bayStage)
			{
			case Building_HighDensityHydro.BayStage.Sowing:
				this.HandleSowing();
				return;
			case Building_HighDensityHydro.BayStage.Growing:
				this.HandleGrowing();
				return;
			case Building_HighDensityHydro.BayStage.Harvest:
				this.HandleHarvest();
				return;
			default:
				return;
			}
		}

		// Token: 0x06000004 RID: 4
		public override string GetInspectString()
		{
			string text = base.GetInspectString();
			text = text + "\nStored Plants: " + this.storedPlants;
			if (this.storedPlants > 0)
			{
				text += "\n" + currentPlantDefToGrow.LabelCap + " | " + string.Format("{0:#0}%", this.growth * 100f);
				//text += "\nGrowth: " + string.Format("{0:#0}%", this.growth * 100f);
				if (HDH_Mod.settings.lightRequirement && this.avgGlow >= 0f)
				{
					text = text + "\nAverage light: " + string.Format("{0:0}%", this.avgGlow * 100f);
				}
			}
			return text;
		}

		// Token: 0x06000007 RID: 7
		public Building_HighDensityHydro()
		{
		}

		// Token: 0x06000009 RID: 9
		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			this.LoadConfig();
			int x = this.def.size.x;
			float y = (x == 1) ? 0.6f : 0.1f;
			this.margin = ((x == 1) ? 0.15f : 0.08f);
			this.barsize = new Vector2((float)this.def.size.z - 0.4f, y);

			drawMatrixCache.Clear();
		}

		// Token: 0x0600000E RID: 14
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look<Building_HighDensityHydro.BayStage>(ref this.bayStage, "bayStage", Building_HighDensityHydro.BayStage.Sowing, false);
			Scribe_Values.Look<int>(ref this.storedPlants, "storedPlants", 0, false);
			Scribe_Values.Look<float>(ref this.growth, "growth", 0f, false);
			Scribe_Defs.Look(ref currentPlantDefToGrow, "queuedPlantDefToGrow");
			
			// if (Scribe.mode == LoadSaveMode.LoadingVars)
			// {
			// 	Log.Warning($"[HDH] Loading hydro: storedPlants={storedPlants}, growth={growth}, stage={bayStage}");
			// }
		}

		// Token: 0x06000012 RID: 18
		public new void SetPlantDefToGrow(ThingDef plantDef)
		{
			base.SetPlantDefToGrow(plantDef);
		}

		// Token: 0x060000DE RID: 222
		public new bool CanAcceptSowNow()
		{
			return base.CanAcceptSowNow() && this.bayStage == Building_HighDensityHydro.BayStage.Sowing && this.storedPlants < this.capacity;
		}
		
		// Token: 0x060000ED RID: 237
		protected override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
		    base.DrawAt(drawLoc, flip);
			
		    // Draw growth bar during Growing stage
		    if (this.storedPlants > 0 && this.bayStage == BayStage.Growing)
		    {
		        GenDraw.FillableBarRequest barReq = new GenDraw.FillableBarRequest
		        {
		            center = drawLoc + Vector3.up * 0.1f,
		            size = this.barsize,
		            fillPercent = this.growth,
		            filledMat = HDH_Graphics.HDHBarFilledMat,
		            unfilledMat = HDH_Graphics.HDHBarUnfilledMat,
		            margin = this.margin,
		            rotation = this.Rotation.Rotated(RotationDirection.Clockwise)
		        };
		        GenDraw.DrawFillableBar(barReq);
		    }

		    if (this.currentPlantDefToGrow == null || this.storedPlants <= 0)
		        return;

		    // Cache the graphic
		    Graphic graphic;
		    if (!plantGraphicCache.TryGetValue(this.currentPlantDefToGrow, out graphic))
		    {
		        graphic = this.currentPlantDefToGrow.graphicData.Graphic;
		        plantGraphicCache[this.currentPlantDefToGrow] = graphic;
		    }

		    // Stable non-wind material
		    Material mat = new Material(graphic.MatSingle);
		    mat.shader = ShaderDatabase.Cutout;

		    // Use in-game ticks
		    int ticks = Find.TickManager.TicksGame;
		    float swayAmplitude = 0.005f;
		    float swaySpeed = 0.1f; // radians per tick (adjust to taste)
		    //float scale = Mathf.Lerp(0.2f, 1.0f, this.growth);

		    List<IntVec3> cells = this.OccupiedRect().Cells.ToList();
		    int cellCount = cells.Count;

		    int visibleCount = 0;
		    if (this.bayStage == BayStage.Growing)
		    {
			    visibleCount = cellCount;
		    }
		    else if (this.bayStage == BayStage.Sowing && this.storedPlants > 0)
		    {
			    visibleCount = Mathf.Clamp(Mathf.FloorToInt((this.storedPlants / (float)this.capacity) * cellCount), 1, cellCount);
		    }
		    else if (this.bayStage == BayStage.Harvest)
		    {
			    visibleCount = 0;
		    }

		    for (int i = 0; i < visibleCount; i++)
		    {
			    IntVec3 cell = cells[i];

			    float phaseOffset = (cell.x * 17 + cell.z * 31) % 100 / 100f;
			    float swayOffset = Mathf.Sin(ticks * swaySpeed + phaseOffset * Mathf.PI * 2f) * swayAmplitude;

			    Vector3 cellDrawPos = cell.ToVector3ShiftedWithAltitude(AltitudeLayer.BuildingOnTop);
			    cellDrawPos.x += swayOffset;

			    float scale = Mathf.Lerp(0.2f, 1.0f, this.growth);

			    Matrix4x4 matrix = Matrix4x4.TRS(
				    cellDrawPos,
				    Quaternion.identity,
				    new Vector3(scale, 1f, scale)
			    );

			    Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
		    }
		}

		// Token: 0x0600012D RID: 301
		private void LoadConfig()
		{
			HydroStatsExtension modExt = this.def.GetModExtension<HydroStatsExtension>();
			if (modExt != null)
			{
				this.capacity = modExt.capacity;
				this.fertility = modExt.fertility;
			}
		}

		// Token: 0x0600012E RID: 302
		private void HandleSowing()
		{
			if (currentPlantDefToGrow == null)
			{
				currentPlantDefToGrow = GetPlantDefToGrow();
			}

			if (currentPlantDefToGrow != GetPlantDefToGrow())
			{
				currentPlantDefToGrow = GetPlantDefToGrow();
				KillAllPlantsAndReset();
			}
			
			foreach (Plant plant in base.PlantsOnMe.ToList<Plant>())
			{
				if (plant.LifeStage != PlantLifeStage.Growing)
					continue;
				
				plant.DeSpawn(DestroyMode.Vanish);
				this.storedPlants++;
			}
			
			if (this.storedPlants >= this.capacity)
			{
				this.storedPlants = this.capacity;
				foreach (Plant plant2 in base.PlantsOnMe.ToList<Plant>())
				{
					plant2.DeSpawn(DestroyMode.Vanish);
				}
				
				// Cancel sowing jobs targeting this grower
				foreach (Pawn pawn in this.Map.mapPawns.AllPawnsSpawned)
				{
					Job curJob = pawn.CurJob;
					if (curJob != null && curJob.def == JobDefOf.Sow && curJob.targetA.HasThing)
					{
						if (curJob.targetA.Thing == this)
						{
							pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
						}
					}
				}
				this.bayStage = Building_HighDensityHydro.BayStage.Growing;
				SoundDefOf.CryptosleepCasket_Accept.PlayOneShot(new TargetInfo(base.Position, base.Map, false));
			}
		}

		// Token: 0x0600012F RID: 303
		private void HandleGrowing()
		{
			if (this.PowerComp != null && !this.PowerComp.PowerOn)
			{
				this.avgGlow = -1f;
				return;
			}
			float temperature = base.Position.GetTemperature(base.Map);
			if (temperature < 10f || temperature > 42f)
			{
				this.avgGlow = -1f;
				return;
			}
			float dayPct = GenLocalDate.DayPercent(this);
			if (dayPct < 0.25f || dayPct > 0.8f)
			{
				this.avgGlow = -1f;
				return;
			}
			ThingDef plantDef = currentPlantDefToGrow;
			if (((plantDef != null) ? plantDef.plant : null) == null)
			{
				this.avgGlow = -1f;
				return;
			}
			
			float growthRate = 0f;
			if (HDH_Mod.settings.lightRequirement)
			{
				float minGlow = plantDef.plant.growMinGlow;
				float optimalGlow = plantDef.plant.growOptimalGlow;
				float totalGlow = 0f;
				int cellCount = 0;
				foreach (IntVec3 cell in this.OccupiedRect().Cells)
				{
					totalGlow += base.Map.glowGrid.GroundGlowAt(cell, false, false);
					cellCount++;
				}
				this.avgGlow = ((cellCount > 0) ? (totalGlow / (float)cellCount) : 1f);
				if (this.avgGlow >= minGlow)
				{
					growthRate = Mathf.Clamp01((this.avgGlow - minGlow) / (optimalGlow - minGlow));
				}
			
				if (growthRate <= 0f)
				{
					return;
				}
			}
			else
			{
				growthRate = 1f;
			}
			
			float growthPerTick = 1f / (60000f * this.growDays()) * 250f;
			this.growth += this.fertility * growthRate * growthPerTick;
			this.growth = Mathf.Clamp01(this.growth);
			if (this.growth >= 1f)
			{
				this.bayStage = Building_HighDensityHydro.BayStage.Harvest;
			}
		}

		// Token: 0x06000130 RID: 304
		private void HandleHarvest()
		{
			foreach (IntVec3 cell in this.OccupiedRect().Cells)
			{
				if (this.storedPlants <= 0)
				{
					break;
				}
				if (!base.Map.thingGrid.ThingsListAt(cell).Any((Thing t) => t is Plant))
				{
					ThingDef plantDef = currentPlantDefToGrow;
					if (plantDef != null)
					{
						((Plant)GenSpawn.Spawn(ThingMaker.MakeThing(plantDef, null), cell, base.Map, WipeMode.Vanish)).Growth = 1f;
						this.storedPlants--;
					}
				}
			}
			if (this.storedPlants == 0)
			{
				this.growth = 0f;

				currentPlantDefToGrow = GetPlantDefToGrow();
				this.bayStage = Building_HighDensityHydro.BayStage.Sowing;
			}
		}

		// Token: 0x06000131 RID: 305
		private float growDays()
		{
			ThingDef plantDef = base.GetPlantDefToGrow();
			if (plantDef == null || plantDef.plant == null)
			{
				return 5f;
			}
			return plantDef.plant.growDays;
		}

		// Token: 0x17000014 RID: 20
		// (get) Token: 0x06000139 RID: 313
		private new CompPowerTrader PowerComp
		{
			get
			{
				if (this.powerCompCached == null)
				{
					this.powerCompCached = base.GetComp<CompPowerTrader>();
				}
				return this.powerCompCached;
			}
		}

		// Token: 0x06000142 RID: 322
		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			//this.KillAllPlantsAndReset();
			base.DeSpawn(mode);
			drawMatrixCache.Clear();
		}

		// Token: 0x0600015B RID: 347
		private void KillAllPlantsAndReset()
		{
			this.storedPlants = 0;
			this.growth = 0f;
			this.bayStage = Building_HighDensityHydro.BayStage.Sowing;
			foreach (Plant plant in base.PlantsOnMe.ToList<Plant>())
			{
				plant.Destroy(DestroyMode.Vanish);
			}
		}

		public int GetHDHCapacity()
		{
			return capacity;
		}

		public int GetNumStoredPlants()
		{
			return storedPlants;
		}

		// Token: 0x040000C8 RID: 200
		private int capacity;

		// Token: 0x040000C9 RID: 201
		private float fertility = 2.8f;

		// Token: 0x040000CA RID: 202
		private Building_HighDensityHydro.BayStage bayStage;

		// Token: 0x040000CB RID: 203
		private Vector2 barsize;

		// Token: 0x040000CC RID: 204
		private float margin;

		// Token: 0x040000CD RID: 205
		private int storedPlants;

		// Token: 0x040000CE RID: 206
		private float growth;

		// Token: 0x040000D1 RID: 209
		private CompPowerTrader powerCompCached;

		// Token: 0x040000E7 RID: 231
		private bool wasPoweredLastTick = true;

		// Token: 0x0400011E RID: 286
		private float avgGlow;

		private ThingDef currentPlantDefToGrow = null;

		// Token: 0x02000009 RID: 9
		private enum BayStage
		{
			// Token: 0x040000C5 RID: 197
			Sowing,
			// Token: 0x040000C6 RID: 198
			Growing,
			// Token: 0x040000C7 RID: 199
			Harvest
		}
	}
}
