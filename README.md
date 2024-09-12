# AI_3D_Models_Grasshopper
Generate AI 3D models inside Grasshopper for Rhino 8


- The maximum prompt length is a few thousand words. Longer prompts usually take longer to respond.
- Shorter prompts, 1-2 sentences in length, succeed more often than longer prompts.
- If your prompt omits important dimensions, Text-to-CAD will make its best guess to fill in missing details.
- Traditional, simple mechanical parts work best right now - fasteners, bearings, connectors, etc.
- Text-to-CAD returns a 500 error code if it fails to generate a valid geometry internally, even if it understands your prompt.
- The same prompt can generate different results when submitted multiple times. Sometimes a failing prompt will succeed on the next attempt, and vice versa.





```python
import bpy
import os

# Set the path to your STL files
stl_folder_path = "/Users/jorgemuyo/Desktop/STUFF/AI_3D_Models_Grasshopper/STL"
output_folder_path = "/Users/jorgemuyo/Desktop/STUFF/AI_3D_Models_Grasshopper/OBJ"  # Set your output path 

# Function to select and delete everything in the scene
def delete_all():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()

# Function to import an STL file
def import_stl(file_path):
    bpy.ops.import_mesh.stl(filepath=file_path)

# Function to create an animation from a sequence of STL files
def create_animation_from_stl_sequence(stl_folder_path):
    stl_files = sorted([f for f in os.listdir(stl_folder_path) if f.startswith('pipe_') and f.endswith('.stl')])
    frame_number = 1
    
    for stl_file in stl_files:
        file_path = os.path.join(stl_folder_path, stl_file)
        
        # Import STL
        import_stl(file_path)
        
        # Get the imported object (assume it's the only one recently added)
        imported_obj = bpy.context.selected_objects[0]
        imported_obj.name = f"Frame_{frame_number}"
        
        # Set keyframe for visibility
        imported_obj.hide_viewport = True
        imported_obj.hide_render = True
        imported_obj.keyframe_insert(data_path="hide_viewport", frame=frame_number-1)
        imported_obj.hide_viewport = False
        imported_obj.hide_render = False
        imported_obj.keyframe_insert(data_path="hide_viewport", frame=frame_number)
        imported_obj.hide_viewport = True
        imported_obj.hide_render = True
        imported_obj.keyframe_insert(data_path="hide_viewport", frame=frame_number+1)
        
        frame_number += 1

# Main function
def main():
    # Clear the current scene
    delete_all()
    
    # Assemble the STL animation sequence
    create_animation_from_stl_sequence(stl_folder_path)

# Execute the main function
main()
```

