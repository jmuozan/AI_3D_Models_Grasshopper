import rhinoscriptsyntax as rs
import Rhino
import os
import scriptcontext as sc
import time

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
    import Rhino
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

# Path to the STEP file from input
step_file_path = x

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
