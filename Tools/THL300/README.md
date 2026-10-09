# THL300 manufacturer CAD conversion

`Assets/THL300/THL300.igs` is the unmodified 5,071,536-byte IGES from the user's
`THL300.zip`. The file identifies the Creo assembly `THL300_3D_ASM`, exported
2021-11-11. Its SHA256 and the mesh SHA256 are recorded in
`Assets/THL300/THL300.metadata.json`.

`export_mesh.py` uses OpenCascade 7.8.1 (`cadquery-ocp==7.8.1.1`) to tessellate
the four actual manifold solids, at 0.25 mm chord deflection and 10 degrees
angular deflection. The runtime uses the resulting embedded `THL300.mesh` and
does not need Python, OpenCascade, SolidWorks, a CAD installation or a network.
Original explicit IGES colors are preserved. Unspecified IGES color rank 0
uses neutral metal RGB 200,208,214. Tiny triangles with no area at float32
precision are omitted; triangle winding is checked against CAD surface normals.

To regenerate, install `cadquery-ocp==7.8.1.1` into a dedicated Python 3.12
environment and run `python Tools/THL300/export_mesh.py`. Add `--preview` to
render the actual CAD using matplotlib and numpy. `inspect_iges.py` reads the
original four solids and their face axes/colors; `verify_mesh.py` validates the
generated resource with Python's standard library.

## Coordinate and joint conventions

The actual IGES is Y-up, with joint-axis native X values -40 (shoulder), -165
(elbow), and -340 (shaft). These are exposed by the source's analytic surfaces
of revolution. The rigid proper rotation is:

```text
canonicalX = -nativeX - 40
canonicalY = nativeZ
canonicalZ = nativeY
```

The resulting coordinates are millimetres, +Z up, mounting plane Z=0, J1 at
X=0, J2 at X=125, shaft X=300. The original CAD is fully extended with the shaft
at its upper position: lower shaft tip Z=112, upper end Z=442. Joint 3 is
positive upward over 0..160 mm, so translate the original shaft by `J3 - 160`;
the physical lower tip becomes `J3 - 48`. The controller's base-coordinate
origin is 48 mm below the mounting plane. Reference rotation angles are zero.

The four source solids are fixed base, first arm, second arm, and shaft. Parent
arm 2 under J1 and J2; parent the shaft under J1, J2, J3 translation and J4
rotation. The complete supplied shaft includes its own flange; no synthetic
gripper or robot body is used. Supplied cable geometry follows its source solid.

Manufacturer drawing: <https://www.shibaura-machine.co.jp/documents/en/product/robot/download/th/pdf_new/S-2GA23-4-THL300-ENG.pdf>.

## Binary format `THL300M1`

All numeric values are little endian. The header consists of 8 ASCII magic
bytes `THL300M1`, 32 raw source-IGES SHA256 bytes, and an Int32 section count.
Each section consists of:

1. Int32 group ID: 0=base, 1=arm1, 2=arm2, 3=shaft.
2. UTF8 name using .NET BinaryWriter's 7-bit encoded byte-length prefix.
3. Four RGBA bytes.
4. Int32 vertex count, Int32 index count.
5. Vertex count interleaved records of six Float32 values: X,Y,Z,NX,NY,NZ.
6. Index count Int32 values. Three consecutive indices form one triangle.

Each original face retains its own nodes and normals, so hard CAD edges remain
sharp. Faces sharing a material and joint are merged into a section. Geometry
coordinates remain in the canonical global neutral pose for nested pivot
transforms in WPF.
