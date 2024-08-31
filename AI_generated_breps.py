import time
import os
import rhinoscriptsyntax as rs
import Rhino
import shutil
import scriptcontext as sc
from datetime import datetime
from kittycad.api.ai import create_text_to_cad, get_text_to_cad_model_for_user
from kittycad.client import Client
from kittycad.models.api_call_status import ApiCallStatus
from kittycad.models.file_export_format import FileExportFormat
from kittycad.models.text_to_cad_create_body import TextToCadCreateBody

API_KEY = api
client = Client(token=API_KEY)

output_dir = path

# Ensure directory exists
os.makedirs(output_dir, exist_ok=True)

# 'a' to determine the output format (Delete this option when I have time)
a = 2  # Change this value to 1, 2, or 3 to get .stl, .step, or .obj. Better leave it as step. it has way better topology

# 'run_code' controls whether the script should run or not
run_code = generate 

if run_code:
    if a == 1:
        output_format = FileExportFormat.STL
        file_extension = "stl"
    elif a == 2:
        output_format = FileExportFormat.STEP
        file_extension = "step"
    elif a == 3:
        output_format = FileExportFormat.OBJ
        file_extension = "obj"
    else:
        raise ValueError("Invalid value for 'a'. Choose 1 for .stl, 2 for .step, or 3 for .obj.")

    # Prompt to generate a 3D mode
    response = create_text_to_cad.sync(
        client=client,
        output_format=output_format,
        body=TextToCadCreateBody(
            prompt=prompt,
        ),
    )

    # Check if the task is complete
    while response.completed_at is None:
        time.sleep(5)

        response = get_text_to_cad_model_for_user.sync(
            client=client,
            id=response.id,
        )

    if response.status == ApiCallStatus.FAILED:
        print(f"Text-to-CAD failed: {response.error}")

    elif response.status == ApiCallStatus.COMPLETED:
        # Print out generated files
        print(f"Text-to-CAD completed and returned {len(response.outputs)} files:")
        for name in response.outputs:
            print(f"  * {name}")

        # Saving data
        final_result = response.outputs[f"source.{file_extension}"]
        output_file_path = os.path.join(output_dir, f"text-to-cad-output.{file_extension}")
        with open(output_file_path, "w", encoding="utf-8") as output_file:
            output_file.write(final_result.get_decoded().decode("utf-8"))
            print(f"Saved to {output_file_path}")
else:
    print("Script execution skipped as run_code False.")


def import_step_file(step_file_path):
    # Ensure the file path is a string
    if not isinstance(step_file_path, str):
        print("Invalid path:", step_file_path)
        return []

    # Ensure the file exists
    if not os.path.exists(step_file_path):
        print("File does not exist:", step_file_path)
        return []

    rs.Command(f"_-Import \"{step_file_path}\" _Enter", False)

    # Delay
    time.sleep(2)  
    sc.doc.Views.Redraw()

    # RhinoCommon
    from Rhino.DocObjects import ObjectEnumeratorSettings

    settings = ObjectEnumeratorSettings()
    settings.IncludeLights = False
    settings.IncludeGrips = False
    settings.IncludeAnnotations = False
    settings.IncludeInstances = False
    settings.IncludeDetails = False
    settings.IncludeHatchPatterns = False
    settings.IncludeMeshes = False
    settings.IncludeCurves = False
    settings.IncludePoints = False
    settings.IncludeSurfaces = False
    settings.IncludeBrep = True  

    # Retrieve all objects
    imported_objects = list(Rhino.RhinoDoc.ActiveDoc.Objects.GetObjectList(settings))
    
    print(f"Found {len(imported_objects)} objects in the document.")

    # Delete objects
    for obj in imported_objects:
        Rhino.RhinoDoc.ActiveDoc.Objects.Delete(obj, True)  

    return imported_objects


def get_breps_from_objects(object_list):
    if object_list is None:
        return []

    breps = []
    for obj in object_list:
        if isinstance(obj.Geometry, Rhino.Geometry.Brep):
            breps.append(obj.Geometry)
        else:
            print(f"Object ID {obj.Id} no Brep, is {type(obj.Geometry)}")
    return breps

def move_and_rename_file(step_file_path):
    main_directory = os.path.dirname(step_file_path)

    # Define the 'MODELS' folder 
    models_folder = os.path.join(main_directory, "MODELS")

    # Check if the 'MODELS' folder exists, if not, create
    if not os.path.exists(models_folder):
        os.makedirs(models_folder)
        print(f"Created folder: {models_folder}")

    # Count the number of existing files in 'MODELS'
    existing_files = [f for f in os.listdir(models_folder) if f.endswith('.step') or f.endswith('.stp')]
    model_number = len(existing_files) + 1

    # Date and time
    current_time = datetime.now().strftime("%Y-%m-%d_%H-%M")

    # New file name
    file_name = f"model-{model_number}_{current_time}.step"

    # Destination path
    destination_path = os.path.join(models_folder, file_name)

    # Move and rename
    shutil.move(step_file_path, destination_path)
    print(f"Moved and renamed file to: {destination_path}")

# Path tofile from input
step_file_path = output_file_path

# Import the file
imported_objects = import_step_file(step_file_path)

# Get Breps from imported objects
breps = get_breps_from_objects(imported_objects)

# Output Breps
if breps:
    a = breps
    print(f"Output {len(breps)} Breps.")
else:
    a = "No Breps imported"
    print("No Breps were imported.")

# Move and rename
move_and_rename_file(step_file_path)