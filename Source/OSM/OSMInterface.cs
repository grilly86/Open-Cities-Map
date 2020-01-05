﻿using Mapper.Curves;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using UnityEngine;

namespace Mapper.OSM
{
    public class OSMInterface
    {
        public RoadMapping mapping;
        private readonly FitCurves fc;

        public Dictionary<long, Vector2> nodes = new Dictionary<long, Vector2>();
        public LinkedList<Way> ways = new LinkedList<Way>();
        
        double tolerance = 10;
        double curveError = 5;

        public OSMInterface(string path, double scale, double tolerance, double curveTolerance, double tiles)
        {
            this.tolerance = tolerance;
            this.curveError = curveTolerance;

            mapping = new RoadMapping(tiles);
            fc = new FitCurves();

            var serializer = new XmlSerializer(typeof(osm));
            var reader = new StreamReader(path);

            var osm = (osm)serializer.Deserialize(reader);
            reader.Dispose();

            Init(osm, scale);
        }

        private void Init(osm osm, double scale)
        {
            mapping.InitBoundingBox(osm.bounds, scale);

            nodes.Clear();
            foreach (var node in osm.node)
            {
                if (!nodes.ContainsKey(node.id) && node.lat != 0 && node.lon != 0)
                {
                    Vector2 pos = Vector2.zero;
                    if (mapping.GetPos(node.lon, node.lat, ref pos))
                    {
                        nodes.Add(node.id, pos);
                    }
                }
            }

            ways.Clear();
            foreach (var way in osm.way)
            {
                RoadTypes rt = RoadTypes.None;
                List<long> points = null;
                int layer = 0;

                string streetName = "";
                if (way != null && way.tag != null) {
                    foreach (var tag in way.tag) {
                        if (tag != null) {
                            if (tag.k.Trim().ToLower() == "name") {
                                streetName = tag.v;
                            }
                        }
                    }
                }
                if (mapping.Mapped(way, ref points, ref rt, ref layer))
                {
                    var currentList = new List<long>();
                    for (var i = 0; i < points.Count; i += 1)
                    {
                        var pp = points[i];
                        if (nodes.ContainsKey(pp))
                        {
                            currentList.Add(pp);
                        }
                        else
                        {
                            if (currentList.Count() > 1 || currentList.Contains(pp))
                            {
                                ways.AddLast(new Way(currentList, rt, layer, streetName));
                                currentList = new List<long>();
                            }
                        }

                    }
                    if (currentList.Count() > 1)
                    {
                        ways.AddLast(new Way(currentList, rt, layer, streetName));
                    }
                }
            }
            
            /*var intersection = new Dictionary<long, List<Way>>();
            foreach (var ww in ways)
            {
                foreach (var pp in ww.nodes)
                {
                    if (!intersection.ContainsKey(pp))
                    {
                        intersection.Add(pp, new List<Way>());
                    }
                    intersection[pp].Add(ww);
                }
            }

            var allSplits = new Dictionary<Way, List<int>>();
            foreach (var inter in intersection)
            {
                if (inter.Value.Count > 1)
                {
                    foreach (var way in inter.Value)
                    {
                        if (!allSplits.ContainsKey(way))
                        {
                            allSplits.Add(way, new List<int>());
                        }
                        allSplits[way].Add(way.nodes.IndexOf(inter.Key));
                    }
                }
            }

            foreach (var waySplits in allSplits)
            {
                SplitWay(waySplits.Key, waySplits.Value);
            }

            BreakWaysWhichAreTooLong();*/
            SimplifyWays();

        }

        private void BreakWaysWhichAreTooLong()
        {
            var allSplits = new Dictionary<Way, List<int>>();
            foreach (var way in ways)
            {
                float length = 0f;
                for (var i = 0; i < way.nodes.Count() - 1; i += 1)
                {
                    length += (nodes[way.nodes[i + 1]] - nodes[way.nodes[i]]).magnitude;
                }
                int segments = Mathf.FloorToInt(length / 100f) + 1;
                float averageLength = length / (float)segments;
                if (segments <= 1)
                {
                    continue;
                }
                length = 0;
                var splits = new List<int>();
                for (var i = 0; i < way.nodes.Count() - 1; i += 1)
                {
                    length += (nodes[way.nodes[i + 1]] - nodes[way.nodes[i]]).magnitude;
                    if (length > averageLength && i != way.nodes.Count - 2)
                    {
                        splits.Add(i + 1);
                        length = 0;
                    }
                }
                if (splits.Any())
                {
                    allSplits.Add(way, splits);
                }
            }

            foreach (var waySplits in allSplits)
            {
                SplitWay(waySplits.Key, waySplits.Value);
            }
        }


        private void SplitWay(Way way, List<int> splits)
        {
            splits = splits.OrderBy(c => c).ToList();
            var index = ways.Find(way);
            for (var i = 0; i < splits.Count(); i += 1)
            {
                var nextIndex = way.nodes.Count() - 1;
                if (i != splits.Count - 1)
                {
                    nextIndex = splits[i + 1];
                }
                var newWay = new Way(way.nodes.GetRange(splits[i], 1 + nextIndex - splits[i]), way.roadTypes, way.layer, way.name);
                ways.AddAfter(index, newWay);
            }
            way.nodes.RemoveRange(splits[0] + 1, way.nodes.Count() - splits[0] - 1);
        }


        private void SimplifyWays()
        {
            foreach (var way in ways)
            {
                var points = new List<Vector2>();
                foreach (var pp in way.nodes)
                {
                    points.Add(nodes[pp]);
                }

                List<Vector2> simplified;
                simplified = Douglas.DouglasPeuckerReduction(points, tolerance);
                if (simplified != null && simplified.Count > 1)
                {
                    way.Update(fc.FitCurve(simplified.ToArray(), curveError));
                }
                else
                {
                }
            }

            var newList = new LinkedList<Way>();
            foreach (var way in ways)
            {
                if (way.valid)
                {
                    newList.AddLast(way);
                }
                this.ways = newList;
            }
        }



    }
}
