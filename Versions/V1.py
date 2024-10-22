import time
import os

from kittycad.api.ai import create_text_to_cad, get_text_to_cad_model_for_user
from kittycad.client import Client
from kittycad.models.api_call_status import ApiCallStatus
from kittycad.models.file_export_format import FileExportFormat
from kittycad.models.text_to_cad_create_body import TextToCadCreateBody

# Set your API key directly in the code
API_KEY = 'bd6b055b-416b-442b-a6c3-82435c74c684'

# Create the client with the provided API key
client = Client(token=API_KEY)

# Define the output directory
output_dir = '/Users/jorgemuyo/Desktop/STUFF/Grasshopper_ZOOCAD/MODELS'

# Ensure the output directory exists
os.makedirs(output_dir, exist_ok=True)

# Prompt the API to generate a 3D model from text
response = create_text_to_cad.sync(
    client=client,
    output_format=FileExportFormat.STEP,
    body=TextToCadCreateBody(
        prompt="Design a gear with 40 teeth",
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

    # Save the STEP data in the specified output directory
    final_result = response.outputs["source.step"]
    output_file_path = os.path.join(output_dir, "text-to-cad-output.step")
    with open(output_file_path, "w", encoding="utf-8") as output_file:
        output_file.write(final_result.get_decoded().decode("utf-8"))
        print(f"Saved output to {output_file_path}")
