"""Create deterministic text/vector, embedded-image and scanned-page PDF fixtures."""

import zlib
from pathlib import Path

output = Path("artifacts/reading-performance/rendering-quality.pdf")
output.parent.mkdir(parents=True, exist_ok=True)
pixels = bytes(component for y in range(256) for x in range(256)
               for component in ((0, 0, 0) if y % 48 < 6 else (x, y, x ^ y)))
image = zlib.compress(pixels)
contents = [
    b"BT /F1 24 Tf 50 730 Td (Rendering quality: text and vectors) Tj ET\n"
    b"0.5 g BT /F1 13 Tf 50 680 Td (Antialiasing: small gray text 0123456789) Tj ET\n"
    b"1 0 0 rg BT /F1 18 Tf 50 630 Td (Red text and colored vectors) Tj ET\n"
    b"0 0 1 RG 0.3 w 50 600 m 550 605 l S\n0.2 0.7 0.4 rg 80 430 380 110 re f\n",
    b"BT /F1 24 Tf 50 730 Td (Embedded image: preserve its colors) Tj ET\n"
    b"q 420 0 0 320 80 300 cm /Im1 Do Q\n"
    b"BT /F1 15 Tf 50 220 Td (Text outside the image remains recolored.) Tj ET\n",
    b"q 612 0 0 792 0 0 cm /Im1 Do Q\n",
]
objects = [b"<< /Type /Catalog /Pages 2 0 R >>",
           b"<< /Type /Pages /Kids [5 0 R 7 0 R 9 0 R] /Count 3 >>",
           b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
           b"<< /Type /XObject /Subtype /Image /Width 256 /Height 256 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length "
           + str(len(image)).encode() + b" >>\nstream\n" + image + b"\nendstream"]
for index, content in enumerate(contents):
    content_id = 6 + index * 2
    objects += [b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> /XObject << /Im1 4 0 R >> >> /Contents "
                + str(content_id).encode() + b" 0 R >>",
                b"<< /Length " + str(len(content)).encode() + b" >>\nstream\n" + content + b"endstream"]
document = bytearray(b"%PDF-1.4\n")
offsets = []
for index, body in enumerate(objects, 1):
    offsets.append(len(document))
    document += str(index).encode() + b" 0 obj\n" + body + b"\nendobj\n"
xref = len(document)
document += f"xref\n0 {len(objects)+1}\n0000000000 65535 f \n".encode()
for offset in offsets:
    document += f"{offset:010d} 00000 n \n".encode()
document += f"trailer\n<< /Size {len(objects)+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode()
output.write_bytes(document)
print(output)
