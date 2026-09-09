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
  foreach (var direction in new[] { new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1) })
  {
   var grains = new byte[25];
   int startX = direction.x < 0 ? 4 : direction.x > 0 ? 0 : 2;
   int startY = direction.y < 0 ? 4 : direction.y > 0 ? 0 : 2;
   for (int depth=0; depth<3; depth++) grains[(startY+direction.y*depth)*5+startX+direction.x*depth]=1;
   var taken = new List<Vector2Int>();
   Check(SandBoardUtility.ExtractFromEdge(grains,null,startX,startY,direction.x,direction.y,1,2,10,taken)==2,
    $"Face {direction.x},{direction.y} extracts two layers");
   Check(SandBoardUtility.ExtractFromEdge(grains,null,startX,startY,direction.x,direction.y,1,2,10,taken)==0
    && grains[(startY+direction.y*2)*5+startX+direction.x*2]==1,
    $"Face {direction.x},{direction.y} cannot reach third layer on later ticks");
  }
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
  Check(enabled==7*121 && !mask[27*33+16] && mask[5*33+16],"U footprint preserves notch and correct vertical orientation");
  var g=new byte[900]; var extracted=new List<Vector2Int>();
  g[0]=1;g[1]=2;g[2]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,null,0,0,1,0,1,2,9,extracted)==1 && g[2]==1,"Side suction stops at another color");
  g=new byte[900];g[29]=1;g[28]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,null,29,0,-1,0,1,2,1,extracted)==1 && g[28]==1,"Right edge respects remaining quota");
  g=new byte[900];g[29*30]=1;g[28*30]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,null,0,29,0,-1,1,2,2,extracted)==2,"Bottom edge scans upward");
  g=new byte[900];g[30]=1;g[60]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,null,0,0,0,1,1,2,2,extracted)==1 && g[60]==1,"Top edge only reaches the first two pixel rows");
  g=new byte[900];g[60]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,null,0,0,0,1,1,2,2,extracted)==0 && g[60]==1,"Empty rows still consume suction depth");
  g=new byte[33*33];g[27*33+27]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,mask,0,27,1,0,1,33,9,extracted)==0 && g[27*33+27]==1,"Suction cannot cross an empty footprint notch");
  var legacy=LevelDataCloneUtility.DeepClone(b);legacy.sandInBoard=false;legacy.sandRegions.Clear();
  var migrated=SandBoardUtility.EmbedPictures(legacy,2);
  Check(migrated.sandRegions.Count==2 && migrated.blocks[0].occupiedCells[0].x==legacy.blocks[0].occupiedCells[0].x,"Migration preserves block positions");
  // Painter and runtime must agree for non-square, holed and disconnected footprints.
  foreach (var shape in new[] {
    new List<Vector2Int>{new Vector2Int(4,7)},
    new List<Vector2Int>{new Vector2Int(4,7),new Vector2Int(5,7),new Vector2Int(6,7)},
    new List<Vector2Int>{new Vector2Int(4,7),new Vector2Int(4,8),new Vector2Int(4,9)},
    new List<Vector2Int>{new Vector2Int(4,7),new Vector2Int(5,7),new Vector2Int(4,8)},
    new List<Vector2Int>{new Vector2Int(4,7),new Vector2Int(6,9)} })
  {
    var selection = new LevelEditorState();
    var region = new SandRegionFile();
    foreach (var cell in shape) { selection.AddCell(cell); region.occupiedCells.Add(new LevelCellCoord(cell.x,cell.y)); }
    selection.GetSelectionBounds(out var bounds);
    var canvas = new TilePaintCanvas(shape,bounds);
    var runtimeMask = SandBoardUtility.Mask(region);
    var painted = new List<byte>(new byte[canvas.Size*canvas.Size]);
    canvas.Fill(painted,new Vector2Int(canvas.Width-1,canvas.Height-1),Vector2Int.zero,2);
    bool equal = true;
    for(int i=0;i<painted.Count;i++) if((painted[i]==2)!=runtimeMask[i])equal=false;
    Check(equal,"Painter fill matches runtime mask: "+shape.Count+" cells / "+bounds.width+"x"+bounds.height);
    var indices = new HashSet<int>();
    bool cellsMatch=true;
    foreach(var cell in shape) {
      var pixels=canvas.CellPixels(cell);
      for(int y=pixels.y;y<pixels.yMax;y++)for(int x=pixels.x;x<pixels.xMax;x++)
        if(!canvas.TryGetIndex(x,y,out int index)||canvas.CellAt(x,y)!=cell||!indices.Add(index))cellsMatch=false;
    }
    Check(cellsMatch&&indices.Count==canvas.PaintableCount&&canvas.PaintableCount==shape.Count*121,"Every selected cell has exactly 121 editable pixels");
    foreach(var cell in shape) {
      var pixels=canvas.CellPixels(cell);
      Check(pixels.width==11&&pixels.height==11,"Every cell is 11 by 11");
    }
    canvas.Fill(painted,new Vector2Int(-10,-10),new Vector2Int(canvas.Width+10,canvas.Height+10),0);
    Check(painted.TrueForAll(v=>v==0),"Erase clips to footprint including reverse/outside drags");
    canvas.Fill(painted,Vector2Int.zero,Vector2Int.zero,1);
    Check(painted.FindAll(v=>v!=0).Count<=1,"Single click changes at most one real tile");
  }
  var wideSelection = new List<Vector2Int>();
  for(int y=0;y<2;y++)for(int x=0;x<6;x++)wideSelection.Add(new Vector2Int(x,y));
  var wide = new TilePaintCanvas(wideSelection,new RectInt(0,0,6,2));
  Check(wide.Width==66 && wide.Height==22 && wide.PaintableCount==12*121,"Level 2 picture 2: 6x2 cells produce 66x22 pixels");
  var small = new TilePaintCanvas(new[]{Vector2Int.zero},new RectInt(0,0,3,2));
  Check(small.Width==33&&small.Height==22,"Level 2 picture 1: 3x2 footprint produces 33x22 pixels");
  var legacyPicture=Level();
  SandBoardUtility.ResizePicture(legacyPicture,66);
  Check(legacyPicture.gridSize==66&&legacyPicture.sandGrid.Count==66*66&&legacyPicture.sandGrid[0]==1&&legacyPicture.sandGrid[65]==2,"Legacy art resamples without losing color orientation");
  var wideRegion=new SandRegionFile{pictureIndex=0};
  foreach(var cell in wideSelection)wideRegion.occupiedCells.Add(new LevelCellCoord(cell.x,cell.y));
  var runtimeLevel=Level();
  runtimeLevel.useAuthoredCollectorBoard=true;
  runtimeLevel.collectorBoard=new BlockXLevelFile{sandInBoard=true,sandRegions=new List<SandRegionFile>{wideRegion}};
  var runtimePictures=runtimeLevel.PrepareRuntimePictures();
  Check(runtimePictures[0].gridSize==66&&runtimePictures[0].sandGrid.Count==66*66,"Runtime retains the footprint resolution instead of shrinking to 35");
  var runtimeCounts=CollectorBoardUtility.CountSand(runtimePictures,runtimeLevel.collectorBoard);
  Check(runtimeCounts[1]+runtimeCounts[2]==12*121,"Collector quotas count 121 grains per filled cell");
  g=new byte[66*66];g[65*66+65]=1;g[65*66+64]=1;
  Check(SandBoardUtility.ExtractFromEdge(g,null,65,65,-1,0,1,2,2,extracted)==2,"Edge collection reaches pixels beyond the old 35px boundary");
  Console.WriteLine($"{checks} checks passed");
 }
}
