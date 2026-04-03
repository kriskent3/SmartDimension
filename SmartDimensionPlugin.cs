using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(SmartDimension.SmartDimensionPlugin))]
[assembly: CommandClass(typeof(SmartDimension.SmartDimCommand))]

namespace SmartDimension
{
    public class SmartDimensionPlugin : IExtensionApplication
    {
        public void Initialize()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage("\nSmartDimension Loaded. Use SMARTDIM command to start.");
        }

        public void Terminate() { }
    }

    public class SmartDimCommand
    {
        private const string DIM_LAYER = "DIMENSION";

        [CommandMethod("SMARTDIM")]
        public void AutoDimensionSelection()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. Selection
            PromptSelectionResult pr = ed.GetSelection();
            if (pr.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // 2. Geometry Analysis
                Extents3d partExtents = new Extents3d();
                bool hasPartExtents = false;
                
                Line horizontalCenter = null;
                Line verticalCenter = null;

                foreach (SelectedObject so in pr.Value)
                {
                    Entity ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                    bool isCenter = false;

                    if (ent is Line line)
                    {
                        string ltName = line.Linetype;
                        string layerName = line.Layer;
                        if (ltName.IndexOf("Center", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            layerName.IndexOf("Center", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isCenter = true;
                            // check if horizontal or vertical
                            if (Math.Abs(line.StartPoint.Y - line.EndPoint.Y) < Math.Abs(line.StartPoint.X - line.EndPoint.X))
                                horizontalCenter = line;
                            else
                                verticalCenter = line;
                        }
                    }

                    if (!isCenter)
                    {
                        if (!hasPartExtents)
                        {
                            partExtents = ent.GeometricExtents;
                            hasPartExtents = true;
                        }
                        else
                        {
                            partExtents.AddExtents(ent.GeometricExtents);
                        }
                    }
                }

                if (!hasPartExtents) return;

                // 3. Layer Management
                EnsureLayerExists(db, tr, DIM_LAYER);

                // 4. Placement Points
                double offset = 10.0; // Distance of dimensions from geometry
                Point3d min = partExtents.MinPoint;
                Point3d max = partExtents.MaxPoint;
                Point3d partCenter = new Point3d((min.X + max.X) / 2, (min.Y + max.Y) / 2, 0);

                // Place Horizontal Dimensions (Placed at the Top of the part)
                if (verticalCenter != null)
                {
                    // Dimension from Vertical Center out to Left Edge
                    double cX = (verticalCenter.StartPoint.X + verticalCenter.EndPoint.X) / 2.0;
                    
                    RotatedDimension hDimLeft = new RotatedDimension { Rotation = 0, XLine1Point = new Point3d(cX, partCenter.Y, 0), XLine2Point = new Point3d(min.X, partCenter.Y, 0), DimLinePoint = new Point3d((cX + min.X)/2, max.Y + offset, 0), DimensionStyle = db.Dimstyle, Layer = DIM_LAYER };
                    btr.AppendEntity(hDimLeft); tr.AddNewlyCreatedDBObject(hDimLeft, true);

                    // Dimension from Vertical Center out to Right Edge
                    RotatedDimension hDimRight = new RotatedDimension { Rotation = 0, XLine1Point = new Point3d(cX, partCenter.Y, 0), XLine2Point = new Point3d(max.X, partCenter.Y, 0), DimLinePoint = new Point3d((cX + max.X)/2, max.Y + offset, 0), DimensionStyle = db.Dimstyle, Layer = DIM_LAYER };
                    btr.AppendEntity(hDimRight); tr.AddNewlyCreatedDBObject(hDimRight, true);
                }
                else
                {
                    // Overall Horizontal dimension
                    RotatedDimension hDim = new RotatedDimension { Rotation = 0, XLine1Point = new Point3d(min.X, partCenter.Y, 0), XLine2Point = new Point3d(max.X, partCenter.Y, 0), DimLinePoint = new Point3d(partCenter.X, max.Y + offset, 0), DimensionStyle = db.Dimstyle, Layer = DIM_LAYER };
                    btr.AppendEntity(hDim); tr.AddNewlyCreatedDBObject(hDim, true);
                }

                // Place Vertical Dimensions (Placed at the Right of the part)
                if (horizontalCenter != null)
                {
                    // Dimension from Horizontal Center up to Top Edge
                    double cY = (horizontalCenter.StartPoint.Y + horizontalCenter.EndPoint.Y) / 2.0;
                    
                    RotatedDimension vDimTop = new RotatedDimension { Rotation = Math.PI / 2.0, XLine1Point = new Point3d(partCenter.X, cY, 0), XLine2Point = new Point3d(partCenter.X, max.Y, 0), DimLinePoint = new Point3d(max.X + offset, (cY + max.Y)/2, 0), DimensionStyle = db.Dimstyle, Layer = DIM_LAYER };
                    btr.AppendEntity(vDimTop); tr.AddNewlyCreatedDBObject(vDimTop, true);

                    // Dimension from Horizontal Center down to Bottom Edge
                    RotatedDimension vDimBot = new RotatedDimension { Rotation = Math.PI / 2.0, XLine1Point = new Point3d(partCenter.X, cY, 0), XLine2Point = new Point3d(partCenter.X, min.Y, 0), DimLinePoint = new Point3d(max.X + offset, (cY + min.Y)/2, 0), DimensionStyle = db.Dimstyle, Layer = DIM_LAYER };
                    btr.AppendEntity(vDimBot); tr.AddNewlyCreatedDBObject(vDimBot, true);
                }
                else
                {
                    // Overall Vertical dimension
                    RotatedDimension vDim = new RotatedDimension { Rotation = Math.PI / 2.0, XLine1Point = new Point3d(partCenter.X, min.Y, 0), XLine2Point = new Point3d(partCenter.X, max.Y, 0), DimLinePoint = new Point3d(max.X + offset, partCenter.Y, 0), DimensionStyle = db.Dimstyle, Layer = DIM_LAYER };
                    btr.AppendEntity(vDim); tr.AddNewlyCreatedDBObject(vDim, true);
                }

                tr.Commit();
                ed.WriteMessage("\nAuto-Dimensioning complete.");
            }
        }

        private void EnsureLayerExists(Database db, Transaction tr, string layerName)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName;
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }
    }
}
