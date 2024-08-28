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

# Set your API key directly in the code
API_KEY = api

# Create the client with the provided API key
client = Client(token=API_KEY)

# Define the output directory
output_dir = path

# Ensure the output directory exists
os.makedirs(output_dir, exist_ok=True)

# Set the variable 'a' to determine the output format
a = 2  # Change this value to 1, 2, or 3 to get .stl, .step, or .obj respectively

# Set the variable 'run_code' to control whether the script should run
run_code = generate  # Change this to False to skip running the script

if run_code:
    # Determine the output format based on the value of 'a'
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

    # Prompt the API to generate a 3D model from text
    response = create_text_to_cad.sync(
        client=client,
        output_format=output_format,
        body=TextToCadCreateBody(
            prompt=prompt,
        ),
    )

    # Polling to check if the task is complete
    while response.completed_at is None:
        # Wait for 5 seconds before checking again
        time.sleep(5)

        # Check the status of the task
        response = get_text_to_cad_model_for_user.sync(
            client=client,
            id=response.id,
        )

    if response.status == ApiCallStatus.FAILED:
        # Print out the error message
        print(f"Text-to-CAD failed: {response.error}")

    elif response.status == ApiCallStatus.COMPLETED:
        # Print out the names of the generated files
        print(f"Text-to-CAD completed and returned {len(response.outputs)} files:")
        for name in response.outputs:
            print(f"  * {name}")

        # Save the data in the specified output directory
        final_result = response.outputs[f"source.{file_extension}"]
        output_file_path = os.path.join(output_dir, f"text-to-cad-output.{file_extension}")
        with open(output_file_path, "w", encoding="utf-8") as output_file:
            output_file.write(final_result.get_decoded().decode("utf-8"))
            print(f"Saved output to {output_file_path}")
else:
    print("Script execution is skipped as run_code is set to False.")


def import_step_file(step_file_path):
    """
    Import a STEP file into Rhino and return a list of object IDs.

    Parameters:
    step_file_path (str): The full path to the STEP file.

    Returns:
    list: A list of object IDs of the imported objects.
    """
    # Ensure the file path is a string
    if not isinstance(step_file_path, str):
        print("Invalid file path:", step_file_path)
        return []

    # Ensure the file exists
    if not os.path.exists(step_file_path):
        print("File does not exist:", step_file_path)
        return []

    # Import the STEP file
    rs.Command(f"_-Import \"{step_file_path}\" _Enter", False)

    # Add delay to ensure the import is complete
    time.sleep(2)  # Increased delay

    # Force a redraw to ensure objects are available
    sc.doc.Views.Redraw()

    # Get all objects in the document using RhinoCommon
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
    settings.IncludeBrep = True  # Include Breps

    # Retrieve all objects
    imported_objects = list(Rhino.RhinoDoc.ActiveDoc.Objects.GetObjectList(settings))
    
    print(f"Found {len(imported_objects)} objects in the document.")

    # Delete the imported objects
    for obj in imported_objects:
        Rhino.RhinoDoc.ActiveDoc.Objects.Delete(obj, True)  # Delete the object and commit changes

    return imported_objects


def get_breps_from_objects(object_list):
    """
    Get Brep objects from Rhino objects.

    Parameters:
    object_list (list): A list of Rhino objects.

    Returns:
    list: A list of Brep objects.
    """
    if object_list is None:
        return []

    breps = []
    for obj in object_list:
        if isinstance(obj.Geometry, Rhino.Geometry.Brep):
            breps.append(obj.Geometry)
        else:
            print(f"Object ID {obj.Id} is not a Brep, it is {type(obj.Geometry)}")
    return breps

def move_and_rename_file(step_file_path):
    """
    Move the STEP file to the 'MODELS' folder and rename it.

    Parameters:
    step_file_path (str): The full path to the STEP file.
    """
    # Get the directory where the STEP file is located
    main_directory = os.path.dirname(step_file_path)

    # Define the 'MODELS' folder path
    models_folder = os.path.join(main_directory, "MODELS")

    # Check if the 'MODELS' folder exists, if not, create it
    if not os.path.exists(models_folder):
        os.makedirs(models_folder)
        print(f"Created folder: {models_folder}")

    # Count the number of existing STEP files in the 'MODELS' folder
    existing_files = [f for f in os.listdir(models_folder) if f.endswith('.step') or f.endswith('.stp')]
    model_number = len(existing_files) + 1

    # Get the current date and time
    current_time = datetime.now().strftime("%Y-%m-%d_%H-%M")

    # Create the new file name
    file_name = f"model-{model_number}_{current_time}.step"

    # Define the destination path
    destination_path = os.path.join(models_folder, file_name)

    # Move and rename the STEP file
    shutil.move(step_file_path, destination_path)
    print(f"Moved and renamed file to: {destination_path}")

# Path to the STEP file from input
step_file_path = output_file_path

# Import the STEP file
imported_objects = import_step_file(step_file_path)

# Get Breps from imported objects
breps = get_breps_from_objects(imported_objects)

# Output Breps to Grasshopper
if breps:
    a = breps
    print(f"Output {len(breps)} Breps.")
else:
    a = "No Breps imported"
    print("No Breps were imported.")

# Move and rename the STEP file
move_and_rename_file(step_file_path)