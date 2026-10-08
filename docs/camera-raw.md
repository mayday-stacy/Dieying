# Camera Raw Filter

Filter > Camera Raw Filter opens a grading panel for the active layer, modelled on the one in Adobe's Camera Raw. It works on any pixel layer, not only on developed RAW files. At the top sit a histogram of the graded layer, a thumbnail that works as a white-balance eyedropper (click a pixel that should be neutral), and a readout of the red, green and blue values under the pointer.

The groups below can be opened and closed. A group that has changes shows an eye, which switches the group off and on without clearing its sliders, so you can compare with and without it. OK applies the grade as it is shown, and the last grade is remembered for the rest of the session. Every slider can be reset to a fresh grade's value.

Save Look… at the bottom of the panel writes the grade as a `.cube` lookup table, at 33 points, that any editor with a Color Lookup can load, Dieying's own included, so a grade made here can go to Lightroom or DaVinci Resolve. A table can only change a color by its color, so Light, Color, Color Grading, Curve, Color Mixer and Calibration go in; Effects, Detail and Optics change pixels by their neighbours or their place and are left out, and the panel and the file say so. The button is disabled while nothing that a table can hold is set, and saving leaves the panel open.

## Light

Exposure (in stops), Contrast, Highlights, Shadows, Whites and Blacks.

## Color

White Balance, Custom or Auto, with Temperature and Tint; Vibrance and Saturation. Auto balances the layer's average color.

## Color Grading

Four wheels, for the shadows, midtones, highlights and the whole picture, each with a hue, a saturation and a luminance. Blending says how far the three ranges overlap, and Balance shifts the boundary between shadows and highlights.

## Effects

- Texture, Clarity and Dehaze.
- Glow: an amount, a style (Diffusion, Bloom or Halation), a range, a spread and a warmth.
- Vignette: an amount, a style (Highlight Priority, Color Priority or Paint Overlay), a midpoint, a roundness, a feather and a highlights protection.
- Grain: an amount, a size and a roughness.

## Curve

A parametric curve with Highlights, Lights, Darks and Shadows sliders and Refine Saturation, and a point curve per channel that you edit as the Curves adjustment.

## Color Mixer

Hue, Saturation or Luminance for each of eight color ranges: reds, oranges, yellows, greens, aquas, blues, purples and magentas.

## Detail

Sharpening with an amount, a radius, a detail and a masking that keeps smooth areas untouched. Noise Reduction for luminance noise (with detail and contrast) and for color noise (with detail and smoothness).

## Optics

Remove Chromatic Aberration and lens profile corrections for distortion and vignetting, a manual distortion, Defringe for purple and green fringes with the hue ranges they cover, and a lens vignette with an amount and a midpoint.

## Calibration

The process version, a shadow tint, and a hue and saturation for each of the red, green and blue primaries.
