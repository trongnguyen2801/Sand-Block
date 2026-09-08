using System;
using System.Collections.Generic;
using SandFlowPuzzle;
using SandFlowPuzzle.BlockAuthoring;
using SandFlowPuzzle.BlockAuthoring.EditorTools;
using UnityEngine;
namespace SandFlowPuzzle { public class SandSimulator { public const int GRID_SIZE = 30; } }
class Memory : ILevelDataRepository {
 public BlockXLevelFile Data; public int Saves;
 public bool TryLoad(string p,out BlockXLevelFile d,out string e){d=LevelDataCloneUtility.DeepClone(Data);e=null;return true;}
 public bool TrySaveAtomic(string p,BlockXLevelFile d,out string e){Data=LevelDataCloneUtility.DeepClone(d);e=null;Saves++;return true;}
}
class Program
{
 static int checks;
 static void Check(bool v,string label){if(!v)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static LevelData Level(){return new LevelData { levelName="Test",gridSize=2,sandGrid=new List<byte>{1,2,1,2},palette=new List<SerializableColor>{new SerializableColor(1,0,0),new SerializableColor(0,0,1)}};}
 static void Main(){
  var level=Level();level.collectorBoard=CollectorBoardUtility.CreateDefault(level);level.useAuthoredCollectorBoard=true;
  Check(CollectorBoardUtility.TryResolve(level,out var resolved,out _),"Default authored layout covers sand colors");
  var counts=CollectorBoardUtility.CountSand(level.PrepareRuntimePictures());
  Check(counts[1]==450&&counts[2]==450,"Quota totals use runtime resampling, not source resolution");
  var mem=new Memory{Data=level.collectorBoard};var doc=new LevelEditorDocument(mem);doc.Load("test",out _);
  var interaction=new BlockInteraction();var grid=new GridEditInteraction();
  Check(!interaction.TryMoveBlock(doc,0,new Vector2Int(2,0),out _),"Reject overlap with another block");
  Check(doc.Revision==0&&mem.Saves==0,"Invalid move is transactional");
  Check(interaction.TryMoveBlock(doc,0,new Vector2Int(1,0),out _),"Valid drag moves entire shape");
  Check(doc.Data.blocks[0].occupiedCells[0].x==1&&doc.Data.blocks[0].occupiedCells[1].x==1,"Anchor translation preserves ordered shape cells");
  Check(mem.Saves==1,"One valid move commits once");
  Check(!grid.TryToggleCell(doc,new Vector2Int(1,0),out _),"Cannot disable occupied cell");
  Check(grid.TryToggleCell(doc,new Vector2Int(0,0),out _),"Can disable free cell");
  Check(!interaction.TryMoveBlock(doc,0,new Vector2Int(0,0),out _),"Cannot move into disabled cells");
  Check(!interaction.TryMoveBlock(doc,0,new Vector2Int(-1,0),out _),"Cannot move outside board");
  Check(!grid.TryResizeGrid(doc,1,6,out _),"Cannot shrink through a block");
  var state=new LevelEditorState();interaction.BeginAdd(1,state);
  Check(interaction.TryAddDraftCell(doc.Data,new Vector2Int(4,2),state,out _),"Add draft on free cell");
  Check(!interaction.TryAddDraftCell(doc.Data,new Vector2Int(4,2),state,out _),"Draft rejects repeated cells");
  int n=doc.Data.blocks.Count;interaction.TryAddDraftCell(doc.Data,new Vector2Int(4,3),state,out _);
  Check(doc.Data.blocks.Count==n,"Draft does not mutate board until Create");
  Check(interaction.CommitDraft(doc,state,out _)&&doc.Data.blocks.Count==n+1,"Create commits ordered block");
  Check(interaction.TryDeleteBlock(doc,n,out _),"Delete selected block");
  level.collectorBoard=doc.Data;
  var old=level.palette[0];level.palette[0]=level.palette[1];level.palette[1]=old;
  Check(CollectorBoardUtility.TryResolve(level,out resolved,out _)&&resolved.blocks[0].colorId==2,"Authored RGB stays stable after picture palette reordering");
  level.collectorBoard.blocks.RemoveAt(0);
  Check(!CollectorBoardUtility.TryResolve(level,out _,out _),"Missing collector color blocks save/play validation");
  var duplicate=LevelDataCloneUtility.DeepClone(doc.Data);duplicate.blocks[0].occupiedCells.Add(duplicate.blocks[0].occupiedCells[0]);
  Check(!BlockXLevelValidator.TryValidate(duplicate,out _),"Imported duplicate cells rejected");
  Check(LevelGridCoordinateUtility.ToIndex(new Vector2Int(0,0),8,6)==42,"BlockX bottom-left coordinates map to top-first JSON");
  level.useAuthoredCollectorBoard=false;
  Check(CollectorBoardUtility.TryResolve(level,out resolved,out _)&&resolved==null,"Legacy automatic mode remains compatible");
  var unified = Level(); unified.collectorBoard=CollectorBoardUtility.CreateDefault(unified); unified.useAuthoredCollectorBoard=true;
  var b=unified.collectorBoard;
  Check(b.sandInBoard && b.sandRegions.Count==1,"Default layout embeds pictures inside board");
  var cloned=LevelDataCloneUtility.DeepClone(b);cloned.sandRegions[0].occupiedCells.Clear();
  Check(b.sandRegions[0].occupiedCells.Count>0,"Region cloning is independent");
  var origin=SandBoardUtility.Bounds(b.sandRegions[0]);
  var m=new Memory{Data=b};var d=new LevelEditorDocument(m);d.Load("x",out _);
  Check(!interaction.TryMoveBlock(d,0,new Vector2Int(origin.x,origin.y),out _),"Cannot drag blocks into sand footprint");
  Check(!grid.TryToggleCell(d,new Vector2Int(origin.x,origin.y),out _),"Cannot disable a sand-region cell");
  var invalid=LevelDataCloneUtility.DeepClone(b);invalid.sandRegions.Add(new SandRegionFile{pictureIndex=1,occupiedCells=new List<LevelCellCoord>(invalid.sandRegions[0].occupiedCells)});
  Check(!BlockXLevelValidator.TryValidate(invalid,out _),"Sand footprints cannot overlap");
  var u=new SandRegionFile{pictureIndex=0,occupiedCells=new List<LevelCellCoord>()};
  for(int y=0;y<3;y++) for(int x=0;x<3;x++) if(y==2||x==0||x==2)u.occupiedCells.Add(new LevelCellCoord(x,y));
  var mask=SandBoardUtility.Mask(u);int enabled=0;foreach(bool bit in mask)if(bit)enabled++;
  Check(enabled==700 && !mask[25*30+15] && mask[5*30+15],"U footprint preserves notch and correct vertical orientation");
  var g=new byte[900]; var extracted=new List<Vector2Int>();
  g[0]=1;g[1]=2;g[2]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,null,0,0,1,0,1,9,extracted)==1 && g[2]==1,"Side suction stops at another color");
  g=new byte[900];g[29]=1;g[28]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,null,29,0,-1,0,1,1,extracted)==1 && g[28]==1,"Right edge respects remaining quota");
  g=new byte[900];g[29*30]=1;g[28*30]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,null,0,29,0,-1,1,2,extracted)==2,"Bottom edge scans upward");
  g=new byte[900];g[30]=1;g[60]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,null,0,0,0,1,1,2,extracted)==2,"Top edge scans through empty grains inward");
  g=new byte[900];g[25*30+25]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,mask,0,25,1,0,1,9,extracted)==0 && g[25*30+25]==1,"Suction cannot cross an empty footprint notch");
  var legacy=LevelDataCloneUtility.DeepClone(b);legacy.sandInBoard=false;legacy.sandRegions.Clear();
  var migrated=SandBoardUtility.EmbedPictures(legacy,2);
  Check(migrated.sandRegions.Count==2 && migrated.blocks[0].occupiedCells[0].x==legacy.blocks[0].occupiedCells[0].x,"Migration preserves block positions");
  Console.WriteLine($"{checks} checks passed");
 }
}
